namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Gold 0x44FC50: 192 file bytes follow the base; no physical marker is written.</summary>
public static class SacredSavedPhysicalLayout
{
    public const int Size = 247;
    public const int Word64Offset = 55;
    /// <summary>Animation selector passed to the creature's animation method at
    /// Gold 45010F, after successful activation. Zero skips both calls.
    /// Outer hero model disable 555C2D resets this native selector to zero;
    /// reactivation does not replay the saved motion automatically.</summary>
    public const int AnimationSelectionOffset = Word64Offset;
    /// <summary>Native +0xF0: sampled ground height, refreshed by Reset3DPosition (Gold 0x44D520).</summary>
    public const int WordF0Offset = 59;
    public const int Word60Offset = 63;
    public const int Word68Offset = 67;
    public const int Word6COffset = 71;
    public const int Word5COffset = 75;
    /// <summary>Native +0x5C palette mask/handle. Canonical SetLevel event 6
    /// calls Gold 0x402490(mask,255); zero returns zero without reading palette
    /// tables. Physical construction initializes this field to zero (0x44A3E7).</summary>
    public const int PaletteMaskOffset = Word5COffset;
    /// <summary>Native physical +0x70 direction vector. Gold 0x450630 projects X/Y through
    /// 0x622620 and atan2 to obtain the movement-end heading; distinct from base +0x28 storage.</summary>
    public const int Vector70Offset = 79;
    /// <summary>Native Velo +0xB4: free-flight velocity; item-arc mode instead stores target XY.</summary>
    public const int VectorB4Offset = 91;
    /// <summary>Native Rot +0xC0: angular velocity. X == -1000 selects the item arc,
    /// with Y = peak model height and Z = duration in seconds (Gold 0x44DED0/0x44DFE0).</summary>
    public const int VectorC0Offset = 103;
    /// <summary>Native +0xCC pending exact model root pivot in world units, distinct from
    /// logical base X/Y; base flag 0x800000 makes the physical network stream select it.</summary>
    public const int VectorCCOffset = 115;
    public const int VectorD8Offset = 127;
    public const int VectorE4Offset = 139;
    /// <summary>Native box[0]/box[1]: current axis-aligned model bounds in the native model camera,
    /// including its orientation and position. These are not model scale or Euler angles.</summary>
    public const int BoundsMinimumOffset = VectorD8Offset;
    public const int BoundsMaximumOffset = VectorE4Offset;
    public const int Word94Offset = 151;
    public const int Word96Offset = 153;
    /// <summary>Signed 16-bit quest index, native +0x94; constructor default -1 (Gold 0x450C50).</summary>
    public const int QuestNumberOffset = Word94Offset;
    /// <summary>Signed 16-bit quest script effect identity, native +0x96 (Gold 0x450E20).</summary>
    public const int QuestEffectOffset = Word96Offset;
    /// <summary>Physical flags at native +0xB0. Equipment installation 0x555E00
    /// uses 0x10/0x08 in its retained-replacement path; detach 0x556A30 suppresses
    /// its returned reference when the removed item has 0x08. Other object types
    /// use this same word for their own physical flags. Creature result/art
    /// initialization 571080 clears mask 0x7800 (57121A), independently of
    /// the outer C0 loader's later 0x2EF07FFF cleanup mask.</summary>
    public const int WordB0Offset = 155;
    public const int ReservedWordsOffset = 159;
    public const int AnimationDoublesOffset = 175;
    /// <summary>4500FE narrows this saved double to the first floating argument.
    /// 5930B0/44A6C0 send it to Granny control parameter 3 (loop count), capped
    /// at 10000 by 44A83A. It is not a duration in seconds.</summary>
    public const int AnimationFirstArgumentDoubleOffset = AnimationDoublesOffset + 8;
    public const int AnimationLoopCountDoubleOffset = AnimationFirstArgumentDoubleOffset;
    /// <summary>4500EE narrows this double to the second argument; 5930B0
    /// multiplies by the creature animation-speed factor, then 413610 applies
    /// metadata/global factors and sends Granny control parameter 2 (speed).</summary>
    public const int AnimationSecondArgumentDoubleOffset = AnimationDoublesOffset;
    public const int AnimationSpeedDoubleOffset = AnimationSecondArgumentDoubleOffset;
    /// <summary>The reader sends this third double directly to Granny control
    /// parameter 0 before activation creates a new animation control. It is
    /// not forwarded as one of the subsequent animation method's arguments.</summary>
    public const int AnimationControlTimeDoubleOffset = AnimationDoublesOffset + 16;
    /// <summary>Granny parameter 0 is the previous control's start time. A new
    /// control starts at zero; the file reader sets this before model activation
    /// replaces that control, so it is not the new clip's elapsed time.</summary>
    public const int PreviousAnimationStartTimeDoubleOffset = AnimationControlTimeDoubleOffset;
    public const int BoneVectorOffset = 199;
    /// <summary>Gold 4500CC reads these 36 saved bone-state bytes into temporary
    /// storage and discards them. They must not replace the activated skeleton.</summary>
    public const int BoneBlockOffset = 211;
    public const int DiscardedBoneStateSize = 36;
}
