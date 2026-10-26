import {DeviceTextureCache} from './webgpu-textures.js';
import {SkinningPipeline} from './webgpu-skinning.js';
import {shaderSource} from './webgpu-shaders.js';

export { registerShader } from './webgpu-shaders.js';
export { readSkinningVertices } from './webgpu-skinning.js';

const shaderNames = ['SacredModel', 'SacredAnimatedModel', 'SacredEffectModel', 'SacredItemParticle',
    'SacredItemGlow', 'SacredInventoryUi', 'PreviewGroundGrid'];
let devicePromise;
let sharedPromise;
export const diagnostics = { frames: 0, draws: 0, errors: [], viewports: 0,
    frameScheduler: 'requestAnimationFrame', frameLoops: 0, frameCallbacks: 0,
    textureUploads: 0, textureUploadBytes: 0, textureCacheHits: 0,
    textureCacheBytes: 0, textureCacheEntries: 0, textureBindings: 0, textureEvictions: 0 };
Object.assign(diagnostics, { skins: 0, skinningSourceBytes: 0, skinningPaletteBytes: 0, skinningDispatches: 0 });
const align = (n, size = 4) => Math.ceil(n / size) * size;
const bytes = view => ArrayBuffer.isView(view) ? view : view.slice();
function copyUpload(view, scratch) {
    if (ArrayBuffer.isView(view)) return view;
    const target = scratch.subarray(0, view.byteLength);
    view.copyTo(target);
    return target;
}

export async function ensureAvailable() {
    devicePromise ??= (async () => {
        if (!navigator.gpu) throw new Error('WebGPU is required. Open this app in a WebGPU-capable browser over HTTPS or localhost.');
        const adapter = await navigator.gpu.requestAdapter({ powerPreference: 'high-performance' });
        if (!adapter) throw new Error('No WebGPU adapter is available.');
        const device = await adapter.requestDevice();
        device.addEventListener('uncapturederror', event => {
            diagnostics.errors.push(event.error.message);
            console.error('[WebGPU]', event.error.message);
        });
        device.lost.then(info => {
            diagnostics.errors.push(`Device lost: ${info.message}`);
            for (const host of document.querySelectorAll('[data-sacred-viewport]')) {
                host.dataset.error = info.message;
                const message = document.createElement('div');
                message.textContent = 'WebGPU device lost. Reload the page to restore previews.';
                host.replaceChildren(message);
            }
        });
        console.info('[WebGPU] Device ready', adapter.info);
        return device;
    })();
    await devicePromise;
}

export function createHost(drag, wheel) {
    const host = document.createElement('div');
    host.dataset.sacredViewport = '';
    host.style.cssText = 'width:100%;height:100%;overflow:hidden;position:relative;background:#0d1917';
    const canvas = document.createElement('canvas');
    canvas.style.cssText = 'width:100%;height:100%;display:block;touch-action:none';
    host.append(canvas);
    host.canvas = canvas;
    canvas.addEventListener('pointerdown', event => {
        if (event.button !== 0) return;
        canvas.setPointerCapture(event.pointerId);
        host.lastX = event.clientX;
    });
    canvas.addEventListener('pointermove', event => {
        if (!canvas.hasPointerCapture(event.pointerId)) return;
        drag((event.clientX - host.lastX) * 0.01);
        host.lastX = event.clientX;
    });
    canvas.addEventListener('pointerup', event => {
        if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
    });
    canvas.addEventListener('wheel', event => { event.preventDefault(); wheel(-event.deltaY / 100); }, { passive: false });
    return host;
}

export function startFrames(host, render) {
    stopFrames(host);
    if (host.disposed) return;
    host.renderFrame = render; diagnostics.frameLoops++;
    const tick = timestamp => {
        host.animationFrame = undefined;
        if (host.disposed || !host.renderFrame) return;
        diagnostics.frameCallbacks++;
        try { host.renderFrame(timestamp); }
        catch (error) { stopFrames(host); diagnostics.errors.push(String(error)); console.error('[WebGPU] Rendering stopped', error); }
        if (!host.disposed && host.renderFrame) host.animationFrame = requestAnimationFrame(tick);
    };
    host.animationFrame = requestAnimationFrame(tick);
}

export function stopFrames(host) {
    if (host.animationFrame !== undefined) cancelAnimationFrame(host.animationFrame);
    host.animationFrame = undefined;
    if (host.renderFrame) { host.renderFrame = null; diagnostics.frameLoops--; }
}

async function shared() {
    sharedPromise ??= (async () => {
        await ensureAvailable();
        const device = await devicePromise;
        const visibility = GPUShaderStage.VERTEX | GPUShaderStage.FRAGMENT;
        const layout = device.createBindGroupLayout({ entries: [
            { binding: 0, visibility, buffer: { type: 'uniform', hasDynamicOffset: true } },
            { binding: 1, visibility, buffer: { type: 'uniform' } },
            ...[2, 3, 5].map(binding => ({ binding, visibility, texture: { sampleType: 'float' } })),
            { binding: 4, visibility, texture: { sampleType: 'unfilterable-float' } },
            { binding: 6, visibility, sampler: { type: 'filtering' } }
        ] });
        const pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [layout] });
        const modules = {};
        for (const name of shaderNames) {
            modules[name] = {};
            for (const stage of name === 'SacredModel' ? ['vertex', 'fragment', 'opaque'] : ['vertex', 'fragment']) {
                const module = device.createShaderModule({ label: `${name}.${stage}`, code: shaderSource(`${name}.${stage}`) });
                const info = await module.getCompilationInfo();
                const errors = info.messages.filter(message => message.type === 'error');
                if (errors.length) throw new Error(errors.map(message => `${name}:${message.lineNum}: ${message.message}`).join('\n'));
                modules[name][stage] = module;
            }
        }
        const format = navigator.gpu.getPreferredCanvasFormat();
        const straight = { color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha' },
            alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' } };
        const premultiplied = { color: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' },
            alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' } };
        const additive = { color: { srcFactor: 'one', dstFactor: 'one' }, alpha: { srcFactor: 'one', dstFactor: 'one' } };
        const definitions = {
            opaque: ['SacredModel', undefined, true, 'back'],
            model: ['SacredModel', straight, true, 'back'],
            animated: ['SacredAnimatedModel', straight, true, 'back'],
            effect: ['SacredEffectModel', premultiplied, true, 'back'],
            particle: ['SacredItemParticle', additive, false, 'none'],
            alpha: ['SacredItemParticle', premultiplied, false, 'none'],
            line: ['SacredItemParticle', premultiplied, false, 'none', 'line-strip'],
            glow: ['SacredItemGlow', additive, false, 'none'],
            ui: ['SacredInventoryUi', straight, false, 'none'],
            grid: ['PreviewGroundGrid', straight, false, 'none']
        };
        const pipelines = {};
        for (const [key, [shader, blend, depthWriteEnabled, cullMode, topology = 'triangle-list']] of Object.entries(definitions)) {
            const buffers = shader === 'PreviewGroundGrid' ? [] : [{ arrayStride: 32, attributes: [
                { shaderLocation: 0, offset: 0, format: 'float32x3' },
                { shaderLocation: 1, offset: 12, format: 'float32x3' },
                { shaderLocation: 2, offset: 24, format: 'float32x2' }
            ] }];
            pipelines[key] = await device.createRenderPipelineAsync({ label: key, layout: pipelineLayout,
                vertex: { module: modules[shader].vertex, entryPoint: 'vs_main', buffers },
                fragment: { module: modules[shader][key === 'opaque' ? 'opaque' : 'fragment'], entryPoint: key === 'opaque' ? 'ps_sdr_opaque' : shader === 'PreviewGroundGrid' ? 'ps_main' : 'ps_sdr', targets: [{ format, blend }] },
                primitive: { topology, cullMode, frontFace: 'ccw', ...(topology === 'line-strip' ? { stripIndexFormat: 'uint16' } : {}) },
                depthStencil: { format: 'depth32float', depthWriteEnabled, depthCompare: key === 'ui' ? 'always' : 'less-equal' }
            });
        }
        const fallback = device.createTexture({ size: [2, 2], format: 'rgba8unorm', usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST });
        device.queue.writeTexture({ texture: fallback }, new Uint8Array([144,174,120,255,95,125,88,255,95,125,88,255,144,174,120,255]), { bytesPerRow: 8 }, [2,2]);
        const lightmap = device.createTexture({ size: [1, 1], format: 'r32float', usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST });
        device.queue.writeTexture({ texture: lightmap }, new Float32Array([0]), { bytesPerRow: 4 }, [1,1]);
        return { device, layout, pipelines, format, fallback, lightmap,
            skinningPipeline: await SkinningPipeline.create(device, diagnostics),
            textureCache: new DeviceTextureCache(device, 128 * 1024 * 1024, diagnostics),
            sampler: device.createSampler({ addressModeU: 'repeat', addressModeV: 'repeat', magFilter: 'linear', minFilter: 'linear' }) };
    })();
    return sharedPromise;
}

export async function initialize(host) {
    const state = await shared().catch(error => { host.textContent = `WebGPU initialization failed: ${error.message}`; throw error; });
    if (host.disposed) return;
    Object.assign(host, state);
    host.context = host.canvas.getContext('webgpu');
    if (!host.context) throw new Error('Cannot create the WebGPU canvas context.');
    host.context.configure({ device: state.device, format: state.format, alphaMode: 'premultiplied', usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC });
    host.constants = host.device.createBuffer({ size: 256 * 4096, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    host.scene = host.device.createBuffer({ size: 96, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST });
    host.meshes = new Map(); host.textures = new Map(); host.groups = new Map();
    host.constantBytes = new Uint8Array(160); host.sceneBytes = new Uint8Array(96);
    diagnostics.viewports++;
    console.info('[WebGPU] Preview initialized with shared Sacred shaders');
}

export function setMesh(host, key, verticesView, indicesView, updateIndices) {
    let mesh = host.meshes.get(key);
    if (!mesh || mesh.vertexSize < align(verticesView.byteLength) || mesh.indexSize < align(indicesView.byteLength)) {
        mesh?.skin?.destroy(); mesh?.vertex.destroy(); mesh?.index.destroy();
        const vertexSize = Math.max(4, align(verticesView.byteLength)), indexSize = Math.max(4, align(indicesView.byteLength));
        mesh = { vertexSize, indexSize,
            vertexBytes: new Uint8Array(vertexSize), indexBytes: new Uint8Array(indexSize),
            vertex: host.device.createBuffer({ size: vertexSize, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST | GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC }),
            index: host.device.createBuffer({ size: indexSize, usage: GPUBufferUsage.INDEX | GPUBufferUsage.COPY_DST }) };
        host.meshes.set(key, mesh); updateIndices = true;
    }
    const vertices = copyUpload(verticesView, mesh.vertexBytes);
    if (vertices.byteLength) host.device.queue.writeBuffer(mesh.vertex, 0, vertices);
    if (updateIndices && indicesView.byteLength) {
        const indices = copyUpload(indicesView, mesh.indexBytes);
        if (indices.byteLength % 4) {
            mesh.indexBytes.set(indices); mesh.indexBytes.fill(0, indices.byteLength, align(indices.byteLength));
            host.device.queue.writeBuffer(mesh.index, 0, mesh.indexBytes, 0, align(indices.byteLength));
        } else host.device.queue.writeBuffer(mesh.index, 0, indices);
    }
}

export function setTexture(host, name, width, height, pixels, key) {
    const previous = host.textures.get(name);
    if (previous?.key === key) {
        if (previous.width !== width || previous.height !== height) throw new Error('A texture identity changed dimensions.');
        diagnostics.textureCacheHits++; return;
    }
    const entry = host.textureCache.acquire(key, width, height, pixels);
    host.textures.set(name, entry); host.groups.clear();
    if (previous) host.textureCache.release(previous);
}

export function clearResources(host) {
    if (!host.meshes) return;
    for (const mesh of host.meshes.values()) { mesh.skin?.destroy(); mesh.vertex.destroy(); mesh.index.destroy(); }
    for (const entry of host.textures.values()) host.textureCache.release(entry);
    host.meshes.clear(); host.textures.clear(); host.groups.clear();
}

export function beginFrame(host, width, height, scene, transparent) {
    width = Math.max(1, width); height = Math.max(1, height);
    if (host.canvas.width !== width || host.canvas.height !== height || !host.depth) {
        host.canvas.width = width; host.canvas.height = height;
        host.depth?.destroy();
        host.depth = host.device.createTexture({ size: [width, height], format: 'depth32float', usage: GPUTextureUsage.RENDER_ATTACHMENT });
    }
    host.device.queue.writeBuffer(host.scene, 0, copyUpload(scene, host.sceneBytes));
    host.encoder = host.device.createCommandEncoder();
    host.skinningPipeline.dispatch(host.encoder, host.meshes);
    host.frameTexture = host.context.getCurrentTexture();
    host.pass = host.encoder.beginRenderPass({
        colorAttachments: [{ view: host.frameTexture.createView(), clearValue: transparent ? [0,0,0,0] : [0.05,0.10,0.09,1], loadOp: 'clear', storeOp: 'store' }],
        depthStencilAttachment: { view: host.depth.createView(), depthClearValue: 1, depthLoadOp: 'clear', depthStoreOp: 'store' }
    });
    host.drawIndex = 0;
}

export function setSkinning(host, vertices, influences, projection, bones, vertexCount) {
    host.skinningPipeline.attach(host.meshes.get('model'), vertices, influences, projection, bones, vertexCount);
}
export function setSkinningPose(host, regular, rigid) {
    host.skinningPipeline.update(host.meshes.get('model'), regular, rigid);
}

export function draw(host, meshKey, pipeline, texture, overlay, glow, start, count, constants) {
    if (count <= 0) return;
    if (host.drawIndex >= 4096) throw new Error('Preview exceeded 4096 draws in a frame.');
    const key = JSON.stringify([texture, overlay, glow]);
    let group = host.groups.get(key);
    if (!group) {
        const resolve = name => (host.textures.get(name)?.texture || host.fallback).createView();
        group = host.device.createBindGroup({ layout: host.layout, entries: [
            { binding: 0, resource: { buffer: host.constants, offset: 0, size: 160 } },
            { binding: 1, resource: { buffer: host.scene } },
            { binding: 2, resource: resolve(texture) }, { binding: 3, resource: resolve(overlay) },
            { binding: 4, resource: host.lightmap.createView() }, { binding: 5, resource: resolve(glow) },
            { binding: 6, resource: host.sampler }
        ] });
        host.groups.set(key, group);
    }
    const offset = host.drawIndex++ * 256;
    host.device.queue.writeBuffer(host.constants, offset, copyUpload(constants, host.constantBytes));
    host.pass.setPipeline(host.pipelines[pipeline]);
    host.pass.setBindGroup(0, group, [offset]);
    if (pipeline === 'grid') host.pass.draw(6);
    else {
        const mesh = host.meshes.get(meshKey);
        host.pass.setVertexBuffer(0, mesh.vertex);
        host.pass.setIndexBuffer(mesh.index, 'uint16');
        host.pass.drawIndexed(count, 1, start);
    }
    diagnostics.draws++;
}

export function endFrame(host) {
    host.pass.end(); host.pass = null;
    host.device.queue.submit([host.encoder.finish()]);
    diagnostics.frames++;
}

export async function readPixels(host) {
    const width = host.canvas.width, height = host.canvas.height, pitch = align(width * 4, 256);
    const buffer = host.device.createBuffer({ size: pitch * height, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ });
    try {
        const encoder = host.device.createCommandEncoder();
        encoder.copyTextureToBuffer({ texture: host.frameTexture }, { buffer, bytesPerRow: pitch }, [width, height]);
        host.device.queue.submit([encoder.finish()]);
        await buffer.mapAsync(GPUMapMode.READ);
        const mapped = new Uint8Array(buffer.getMappedRange());
        const rgba = new Uint8Array(width * height * 4);
        for (let row = 0; row < height; row++) rgba.set(mapped.subarray(row * pitch, row * pitch + width * 4), row * width * 4);
        if (host.format.startsWith('bgra')) for (let i = 0; i < rgba.length; i += 4) [rgba[i], rgba[i+2]] = [rgba[i+2], rgba[i]];
        return rgba;
    } finally { buffer.destroy(); }
}

export function screenshot(host, name) {
    // RenderFrame is invoked by the C# screenshot cheat immediately before this.
    host.canvas.toBlob(blob => {
        if (!blob) { console.error('[WebGPU] Screenshot encoding failed'); return; }
        const url = URL.createObjectURL(blob), link = document.createElement('a');
        link.href = url; link.download = name.split(/[\\/]/).pop(); link.click();
        setTimeout(() => URL.revokeObjectURL(url), 10000);
        console.info('[WebGPU] Screenshot saved', name);
    }, 'image/png');
}

export function downloadBytes(png, name) {
    const blob = new Blob([bytes(png)], { type: 'image/png' });
    const url = URL.createObjectURL(blob), link = document.createElement('a');
    link.href = url; link.download = name; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function destroy(host) {
    if (host.disposed) return;
    host.disposed = true;
    stopFrames(host);
    clearResources(host);
    host.depth?.destroy(); host.constants?.destroy(); host.scene?.destroy();
    host.context?.unconfigure(); host.remove();
    if (host.device) diagnostics.viewports--;
}
