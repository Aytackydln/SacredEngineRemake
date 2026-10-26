let nextFile = 1;
const installations = new Map();
const files = new Map();
export const metrics = { bytesRead: 0, rangeReads: 0, largestRead: 0,
    singleReads: 0, batchReads: 0, batchRangeReads: 0, compositeReads: 0, maxConcurrentReads: 0 };
let activeReads = 0;
const maxBatchRanges = 256, maxBatchBytes = 64 * 1024;
const maxReadBytes = 64 * 1024, maxGap = 4 * 1024, concurrency = 16;

export function copyBytes(source, destination) { destination.set(source); }

// Keep the original File objects. File.slice() reads only the requested range,
// including offsets above 2 GiB, without a whole-archive ArrayBuffer or MEMFS copy.
export function registerInstallation(selected) {
    const id = crypto.randomUUID();
    const entries = [];
    for (const file of selected) {
        const fileId = nextFile++;
        files.set(fileId, file);
        const path = file.webkitRelativePath || file.name;
        entries.push({ id: fileId, path, length: file.size });
    }
    installations.set(id, entries);
    return JSON.stringify({ id, entries });
}

export function pickInstallation() {
    return new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file';
        input.webkitdirectory = true;
        input.multiple = true;
        input.onchange = () => { resolve(registerInstallation(input.files)); input.remove(); };
        input.oncancel = () => { reject(new Error('Installation selection cancelled.')); input.remove(); };
        input.style.display = 'none';
        document.body.append(input);
        input.click();
    });
}

export async function readRange(id, offset, count) {
    const file = files.get(id);
    if (!file) throw new Error('The selected installation has been released.');
    validateRange(file, offset, count);
    metrics.singleReads++;
    return readFileRange(file, offset, count);
}

function validateRange(file, offset, count) {
    if (!Number.isSafeInteger(offset) || offset < 0 || !Number.isSafeInteger(count) || count < 0 || offset > file.size - count)
        throw new RangeError('Invalid file range.');
}

async function readFileRange(file, offset, count) {
    activeReads++;
    metrics.maxConcurrentReads = Math.max(metrics.maxConcurrentReads, activeReads);
    try {
        const data = new Uint8Array(await file.slice(offset, offset + count).arrayBuffer());
        if (data.byteLength !== count) throw new Error('The browser returned an incomplete file range.');
        metrics.bytesRead += data.byteLength;
        metrics.rangeReads++;
        metrics.largestRead = Math.max(metrics.largestRead, data.byteLength);
        return data;
    } finally { activeReads--; }
}

// Format-agnostic scatter I/O: C# supplies offsets/lengths and decodes the result.
// Batch buffers contain only requested bytes. Nearby ranges share a bounded read;
// sparse ranges run concurrently without racing the C# stream's seek position.
export async function readRanges(id, requests) {
    const file = files.get(id);
    if (!file) throw new Error('The selected installation has been released.');
    if (requests.length % 2 || requests.length > maxBatchRanges * 2) throw new RangeError('Invalid metadata batch.');
    const ranges = [];
    let size = 0;
    for (let index = 0; index < requests.length; index += 2) {
        const offset = requests[index], length = requests[index + 1];
        validateRange(file, offset, length);
        if (size + length > maxBatchBytes) throw new RangeError('Metadata batch exceeds its bounded size.');
        if (length) ranges.push({ offset, length, destination: size });
        size += length;
    }
    metrics.batchReads++;
    metrics.batchRangeReads += requests.length / 2;
    if (file instanceof Blob) {
        // Blob parts reference the original file slices; only the compact batch
        // is materialized. One browser read replaces hundreds of tiny promises,
        // without reading the intervening texture/model payloads.
        const batch = new Blob(ranges.map(range => file.slice(range.offset, range.offset + range.length)));
        metrics.compositeReads++;
        return readFileRange(batch, 0, size);
    }
    // Range-backed providers (including the integration fixtures) cannot be
    // composed as native Blobs. Keep their asynchronous I/O bounded as well.
    ranges.sort((a, b) => a.offset - b.offset);
    const groups = [];
    for (const range of ranges) {
        let group = groups.at(-1);
        const end = range.offset + range.length;
        if (!group || range.offset > group.end + maxGap || end - group.offset > maxReadBytes) {
            group = { offset: range.offset, end, ranges: [] };
            groups.push(group);
        }
        group.end = Math.max(group.end, end);
        group.ranges.push(range);
    }
    const result = new Uint8Array(size);
    let next = 0;
    const workers = Array.from({ length: Math.min(concurrency, groups.length) }, async () => {
        while (next < groups.length) {
            const group = groups[next++];
            const bytes = await readFileRange(file, group.offset, group.end - group.offset);
            for (const range of group.ranges) {
                const start = range.offset - group.offset;
                result.set(bytes.subarray(start, start + range.length), range.destination);
            }
        }
    });
    // Drain outstanding reads before propagating failure, so releasing an
    // installation cannot leave an unobserved rejected read in the background.
    const completed = await Promise.allSettled(workers);
    const failure = completed.find(worker => worker.status === 'rejected');
    if (failure) throw failure.reason;
    return result;
}

export function releaseInstallation(id) {
    for (const entry of installations.get(id) || []) files.delete(entry.id);
    installations.delete(id);
}
