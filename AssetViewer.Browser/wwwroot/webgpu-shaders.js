// Sources are compiled from shared HLSL by the C# incremental generator.
const shaders = new Map();
export function registerShader(name, source) {
    if (!name || !source) throw new Error('A WebGPU shader name and source are required.');
    shaders.set(name, source);
}
export function shaderSource(name) {
    const source = shaders.get(name);
    if (!source) throw new Error(`Missing generated WebGPU shader ${name}.`);
    return source;
}
