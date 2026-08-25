using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal static class WaveVolumeScaler
{
    private const ushort PcmFormat = 0x0001;
    private const ushort ImaAdpcmFormat = 0x0011;

    private static readonly int[] IndexAdjustments =
        [-1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8];

    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544,
        598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878,
        2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894,
        6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818,
        18500, 20350, 22385, 24623, 27086, 29794, 32767
    ];

    public static byte[] Scale(byte[] wave, float volume)
    {
        if (volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume));

        var format = ReadFormat(wave);
        return format.FormatTag switch
        {
            PcmFormat when format.BitsPerSample == 16 => ScalePcm16(wave, format.DataOffset, format.DataLength, volume),
            ImaAdpcmFormat when format.BitsPerSample == 4 => DecodeImaAdpcm(wave, format, volume),
            _ => throw new NotSupportedException(
                $"Inventory WAV format {format.FormatTag}, {format.BitsPerSample}-bit is not supported.")
        };
    }

    private static byte[] ScalePcm16(byte[] wave, int dataOffset, int dataLength, float volume)
    {
        var result = (byte[])wave.Clone();
        var samples = result.AsSpan(dataOffset, dataLength);
        for (var offset = 0; offset + 1 < samples.Length; offset += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(samples[offset..]);
            BinaryPrimitives.WriteInt16LittleEndian(samples[offset..], (short)MathF.Round(sample * volume));
        }
        return result;
    }

    private static byte[] DecodeImaAdpcm(byte[] wave, WaveFormat format, float volume)
    {
        if (format.Channels != 1)
            throw new NotSupportedException("Only mono IMA ADPCM inventory sounds are supported.");
        if (format.BlockAlign < 4)
            throw new InvalidDataException("IMA ADPCM block alignment is smaller than its block header.");

        var samples = new List<short>(format.DataLength * 2);
        var data = wave.AsSpan(format.DataOffset, format.DataLength);
        for (var blockOffset = 0; blockOffset < data.Length; blockOffset += format.BlockAlign)
        {
            var block = data.Slice(blockOffset, Math.Min(format.BlockAlign, data.Length - blockOffset));
            if (block.Length < 4)
                break;

            var predictor = (int)BinaryPrimitives.ReadInt16LittleEndian(block);
            var stepIndex = Math.Clamp(block[2], 0, StepTable.Length - 1);
            samples.Add(ScaleSample(predictor, volume));

            for (var offset = 4; offset < block.Length; offset++)
            {
                predictor = DecodeNibble(block[offset] & 0x0f, predictor, ref stepIndex);
                samples.Add(ScaleSample(predictor, volume));
                predictor = DecodeNibble(block[offset] >> 4, predictor, ref stepIndex);
                samples.Add(ScaleSample(predictor, volume));
            }
        }

        return CreatePcmWave(samples, format.SampleRate, format.Channels);
    }

    private static int DecodeNibble(int nibble, int predictor, ref int stepIndex)
    {
        var step = StepTable[stepIndex];
        var difference = step >> 3;
        if ((nibble & 1) != 0) difference += step >> 2;
        if ((nibble & 2) != 0) difference += step >> 1;
        if ((nibble & 4) != 0) difference += step;

        predictor = (nibble & 8) != 0 ? predictor - difference : predictor + difference;
        predictor = Math.Clamp(predictor, short.MinValue, short.MaxValue);
        stepIndex = Math.Clamp(stepIndex + IndexAdjustments[nibble], 0, StepTable.Length - 1);
        return predictor;
    }

    private static short ScaleSample(int sample, float volume) => (short)MathF.Round(sample * volume);

    private static byte[] CreatePcmWave(IReadOnlyList<short> samples, int sampleRate, ushort channels)
    {
        var dataLength = checked(samples.Count * sizeof(short));
        var result = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length - 8);
        "WAVEfmt "u8.CopyTo(result.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(20), PcmFormat);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(22), channels);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), sampleRate);
        var blockAlign = checked((ushort)(channels * sizeof(short)));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), sampleRate * blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(32), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(34), 16);
        "data"u8.CopyTo(result.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), dataLength);
        for (var index = 0; index < samples.Count; index++)
            BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(44 + index * 2), samples[index]);
        return result;
    }

    private static WaveFormat ReadFormat(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 12 || !wave[..4].SequenceEqual("RIFF"u8) || !wave.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Sound payload is not a RIFF/WAVE file.");

        ushort formatTag = 0;
        ushort channels = 0;
        int sampleRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        var dataOffset = 0;
        var dataLength = 0;
        for (var offset = 12; offset + 8 <= wave.Length;)
        {
            var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(wave.Slice(offset + 4, 4));
            if (chunkLength < 0 || offset + 8L + chunkLength > wave.Length)
                throw new InvalidDataException("WAV chunk extends outside the sound payload.");

            var chunkId = wave.Slice(offset, 4);
            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunkLength < 16)
                    throw new InvalidDataException("WAV format chunk is too short.");
                var chunk = wave.Slice(offset + 8, chunkLength);
                formatTag = BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(chunk[4..]);
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]);
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]);
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                dataOffset = offset + 8;
                dataLength = chunkLength;
            }

            offset = checked(offset + 8 + chunkLength + (chunkLength & 1));
        }

        if (formatTag == 0 || channels == 0 || sampleRate <= 0 || dataOffset == 0)
            throw new InvalidDataException("WAV format or data chunk is missing.");
        return new WaveFormat(formatTag, channels, sampleRate, blockAlign, bitsPerSample, dataOffset, dataLength);
    }

    private readonly record struct WaveFormat(
        ushort FormatTag,
        ushort Channels,
        int SampleRate,
        ushort BlockAlign,
        ushort BitsPerSample,
        int DataOffset,
        int DataLength);
}
