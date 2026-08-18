using System.Numerics;

namespace Sacred.Granny.Meshes;

public readonly record struct VertexPositionNormalTexture(Vector3 Position, Vector3 Normal, Vector2 TexCoord);
