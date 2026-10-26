// One device cache shared by viewports and equipment slots. Active bindings are
// pinned; only idle entries can be evicted. Keys identify C# pixel arrays, not
// material aliases or animation settings, which can differ for the same texture.
export class DeviceTextureCache {
    constructor(device, capacityBytes, diagnostics) {
        this.device = device; this.capacityBytes = capacityBytes; this.diagnostics = diagnostics;
        this.entries = new Map(); this.bytes = 0;
    }
    acquire(key, width, height, pixels) {
        let entry = this.entries.get(key);
        if (entry) {
            if (entry.width !== width || entry.height !== height) throw new Error('A texture identity changed dimensions.');
            this.entries.delete(key); this.entries.set(key, entry);
            this.diagnostics.textureCacheHits++;
        } else {
            const texture = this.device.createTexture({ size: [width, height], format: 'rgba8unorm',
                usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST });
            try {
                this.device.queue.writeTexture({ texture }, ArrayBuffer.isView(pixels) ? pixels : pixels.slice(),
                    { bytesPerRow: width * 4 }, [width, height]);
            } catch (error) { texture.destroy(); throw error; }
            entry = { key, texture, width, height, bytes: width * height * 4, references: 0 };
            this.entries.set(key, entry); this.bytes += entry.bytes;
            this.diagnostics.textureUploads++;
            this.diagnostics.textureUploadBytes += entry.bytes;
        }
        entry.references++; this.diagnostics.textureBindings++;
        this.trim(); return entry;
    }
    release(entry) {
        if (entry.references <= 0) throw new Error('A texture binding was released twice.');
        entry.references--; this.diagnostics.textureBindings--;
        this.trim();
    }
    trim() {
        for (const [key, entry] of this.entries) {
            if (this.bytes <= this.capacityBytes) break;
            if (entry.references) continue;
            this.entries.delete(key); entry.texture.destroy(); this.bytes -= entry.bytes;
            this.diagnostics.textureEvictions++;
        }
        this.diagnostics.textureCacheBytes = this.bytes;
        this.diagnostics.textureCacheEntries = this.entries.size;
    }
}
