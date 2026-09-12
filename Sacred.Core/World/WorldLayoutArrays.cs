using System.Runtime.CompilerServices;
using Sacred.Core.World.Sector;

namespace Sacred.Core.World;

[InlineArray(32)] public struct WorldFileChunks { private WorldFileChunkLayout _first; }
[InlineArray(8)] public struct SectorNeighbors { private ushort _first; }
[InlineArray(4)] public struct SectorWeatherEntries { private SectorWeatherLayout _first; }
[InlineArray(4)] public struct SectorSpawnEntries { private SectorSpawnLayout _first; }
[InlineArray(7)] public struct WorldBytes7 { private byte _first; }
[InlineArray(20)] public struct WorldBytes20 { private byte _first; }
[InlineArray(23)] public struct WorldBytes23 { private byte _first; }
