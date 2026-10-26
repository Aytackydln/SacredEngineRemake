import {shaderSource} from './webgpu-shaders.js';

// The generated compute shader uses the desktop's SacredSkinning.hlsl and its
// packed bind-data ABI. Pose sampling and effect attachments remain in C#.
export class SkinningPipeline {
    static async create(device, diagnostics) {
        const module = device.createShaderModule({ label: 'SacredSkinning', code: shaderSource('SacredSkinning.compute') });
        const info = await module.getCompilationInfo();
        const errors = info.messages.filter(message => message.type === 'error');
        if (errors.length) throw new Error(errors.map(message => `SacredSkinning:${message.lineNum}: ${message.message}`).join('\n'));
        const layout = device.createBindGroupLayout({ entries: [3, 4, 5, 6, 7, 8].map(binding => ({
            binding, visibility: GPUShaderStage.COMPUTE, buffer: { type: binding === 8 ? 'storage' : 'read-only-storage' }
        })) });
        const pipeline = await device.createComputePipelineAsync({ label: 'SacredSkinning',
            layout: device.createPipelineLayout({ bindGroupLayouts: [layout] }), compute: { module, entryPoint: 'skin_main' } });
        return new SkinningPipeline(device, layout, pipeline, diagnostics);
    }
    constructor(device, layout, pipeline, diagnostics) {
        this.device = device; this.layout = layout; this.pipeline = pipeline; this.diagnostics = diagnostics;
    }
    attach(mesh, vertices, influences, projection, boneCount, vertexCount) {
        if (mesh.vertexSize < vertexCount * 32) throw new Error('Skinning output exceeds the model vertex buffer.');
        if (vertices.byteLength !== vertexCount * 48 || projection.byteLength !== 32 || influences.byteLength % 8)
            throw new Error('Skinning source does not match the desktop buffer layout.');
        mesh.skin?.destroy();
        mesh.skin = null;
        const buffers = [];
        const buffer = (size, data) => {
            const result = this.device.createBuffer({ size: Math.max(4, size), usage: GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST });
            buffers.push(result);
            if (data) this.device.queue.writeBuffer(result, 0, ArrayBuffer.isView(data) ? data : data.slice());
            return result;
        };
        try {
            const source = buffer(vertices.byteLength, vertices), weights = buffer(influences.byteLength, influences);
            const transform = buffer(projection.byteLength, projection);
            const regular = buffer(boneCount * 64), rigid = buffer(boneCount * 64);
            const group = this.device.createBindGroup({ layout: this.layout, entries: [
                { binding: 3, resource: { buffer: source } }, { binding: 4, resource: { buffer: weights } },
                { binding: 5, resource: { buffer: transform } }, { binding: 6, resource: { buffer: regular } },
                { binding: 7, resource: { buffer: rigid } }, { binding: 8, resource: { buffer: mesh.vertex } }
            ] });
            const diagnostics = this.diagnostics;
            let disposed = false;
            mesh.skin = { regular, rigid, vertexCount, group, dirty: false,
                regularBytes: new Uint8Array(boneCount * 64), rigidBytes: new Uint8Array(boneCount * 64),
                destroy() { if (disposed) return; disposed = true; buffers.forEach(buffer => buffer.destroy()); diagnostics.skins--; } };
            diagnostics.skins++;
            diagnostics.skinningSourceBytes += vertices.byteLength + influences.byteLength + projection.byteLength;
        } catch (error) { buffers.forEach(buffer => buffer.destroy()); throw error; }
    }
    update(mesh, regular, rigid) {
        const skin = mesh.skin;
        if (!skin || regular.byteLength !== skin.regularBytes.length || rigid.byteLength !== skin.rigidBytes.length)
            throw new Error('Skinning palette does not match the character skeleton.');
        const copy = (view, target) => { if (ArrayBuffer.isView(view)) target.set(view); else view.copyTo(target); return target; };
        this.device.queue.writeBuffer(skin.regular, 0, copy(regular, skin.regularBytes));
        this.device.queue.writeBuffer(skin.rigid, 0, copy(rigid, skin.rigidBytes));
        skin.dirty = true;
        this.diagnostics.skinningPaletteBytes += regular.byteLength + rigid.byteLength;
    }
    dispatch(encoder, meshes) {
        for (const mesh of meshes.values()) {
            const skin = mesh.skin;
            if (!skin?.dirty) continue;
            const pass = encoder.beginComputePass({ label: 'SacredSkinning' });
            pass.setPipeline(this.pipeline); pass.setBindGroup(0, skin.group);
            pass.dispatchWorkgroups(Math.ceil(skin.vertexCount / 64)); pass.end();
            skin.dirty = false; this.diagnostics.skinningDispatches++;
        }
    }
}

export async function readSkinningVertices(host) {
    const mesh = host.meshes.get('model');
    if (!mesh?.skin) throw new Error('This viewport has no GPU-skinned character.');
    const size = mesh.skin.vertexCount * 32;
    const buffer = host.device.createBuffer({ size, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    try {
        const encoder = host.device.createCommandEncoder();
        encoder.copyBufferToBuffer(mesh.vertex, 0, buffer, 0, size);
        host.device.queue.submit([encoder.finish()]);
        await buffer.mapAsync(GPUMapMode.READ);
        return new Uint8Array(buffer.getMappedRange()).slice();
    } finally { buffer.destroy(); }
}
