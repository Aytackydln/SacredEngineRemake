using System;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics;

/// <summary>Uploads only model root-constant ranges whose bitwise values changed in this pass.</summary>
internal sealed class ModelRootConstantsUpdater
{
    private readonly RootConstantSlot[] _slots;

    public ModelRootConstantsUpdater(int rootParameterCount)
    {
        _slots = new RootConstantSlot[rootParameterCount];
        for (var index = 0; index < _slots.Length; index++)
            _slots[index] = new RootConstantSlot();
    }

    public void Reset()
    {
        foreach (var slot in _slots)
            slot.Reset();
    }

    public unsafe void SetIfChanged(
        ID3D12GraphicsCommandList commandList,
        int rootParameterIndex,
        float* values,
        int count,
        int destinationOffset)
    {
        var slot = _slots[rootParameterIndex];
        if (slot.Matches(values, count, destinationOffset))
            return;

        commandList.SetGraphicsRoot32BitConstants(
            (uint)rootParameterIndex,
            (uint)count,
            (nint)values,
            (uint)destinationOffset);
        slot.Store(values, count, destinationOffset);
    }

    private sealed class RootConstantSlot
    {
        private int[] _bits = [];
        private bool[] _known = [];

        public void Reset() => Array.Clear(_known);

        public unsafe bool Matches(float* values, int count, int destinationOffset)
        {
            if (destinationOffset < 0 || count < 0 || destinationOffset + count > _bits.Length)
                return false;

            for (var index = 0; index < count; index++)
            {
                var destinationIndex = destinationOffset + index;
                if (!_known[destinationIndex] || _bits[destinationIndex] != BitConverter.SingleToInt32Bits(values[index]))
                    return false;
            }

            return true;
        }

        public unsafe void Store(float* values, int count, int destinationOffset)
        {
            EnsureCapacity(destinationOffset + count);
            for (var index = 0; index < count; index++)
            {
                var destinationIndex = destinationOffset + index;
                _bits[destinationIndex] = BitConverter.SingleToInt32Bits(values[index]);
                _known[destinationIndex] = true;
            }
        }

        private void EnsureCapacity(int length)
        {
            if (_bits.Length >= length)
                return;

            Array.Resize(ref _bits, length);
            Array.Resize(ref _known, length);
        }
    }
}
