using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Items;
using Sacred.Assets.Paks.Models;
using Sacred.Particles.Particles;
using Sacred.Granny.Animation;
using Sacred.Inventory.Effects;
using Sacred.Particles;
using Sacred.Engine.Scene;

internal static class SimulationVerification
{
    public static async Task Run(ModelsPakArchive models, IReadOnlyDictionary<ushort, ItemsPakEntry> items)
    {
        Check(Marshal.SizeOf<SacredModelParticleStateLayout>() == 0xA0, "particle state size");
        Check(Marshal.SizeOf<SacredModelTrailStateLayout>() == 0x4BC, "trail state size");
        Check(Marshal.SizeOf<SacredModelTrailPointLayout>() == 0x18, "point stride");
        var baseName = items[1].ModelName;
        var clip = await models.LoadCharacterAnimationAsync(baseName, CharacterMotionKind.Idle, CharacterMotionWeaponStyle.OneHanded);
        Check(clip is not null, "real animation loaded from Models.tmp");
        foreach (var id in new ushort[] { 1771, 3072, 3073, 3809, 4010, 4092, 5632, 5633 })
        {
            var definition = SacredModelEffectCatalogue.Find(id)!;
            var wing = definition.Kind == SacredModelEffectKind.Streak;
            var model = await models.LoadCharacterBaseModelAsync(baseName,
                [new(items[id].ModelName, wing ? null : "Bip01 R Hand", wing ? null : "Bone_weapon_01")]);
            var scene = EquipmentEffectSceneFactory.Create(model,
                [new EquipmentEffectAttachment(1, items[id].ModelName, wing ? null : "Bip01 R Hand", default, 40) { ItemId = id }]);
            Check(scene is not null, $"{id} scene");
            var pose = new GrnAnimatedMesh(model.Mesh!, model.Skin!, clip!);
            for (var frame = 0; frame < 240; frame++)
            {
                pose.Apply(frame / 60f);
                scene!.ApplyPose(pose, 1f / 60);
            }
            Check(scene!.Mesh.Vertices.All(v => float.IsFinite(v.Position.LengthSquared())), $"{id} finite vertices");
            Check(scene.Surfaces.Any(s => s.Color.W > 0), $"{id} visible after simulation");
            if (definition.Kind == SacredModelEffectKind.Torch)
            {
                Check(definition.ParticleAtlasSide == 4 && definition.ParticleDrawFlags == 0x15,
                    "torch stdRender arguments recovered from executable");
                Check(definition.LensFlareTextureName == "PARTICLE_GLOW01.TGA",
                    "torch stdLensflare type 3 texture");
                var liveCells = new HashSet<int>();
                var rotated = false;
                for (var i = 0; i < scene.Surfaces.Count - 1; i++)
                {
                    if (scene.Surfaces[i].Color.W <= 0) continue;
                    var quad = scene.Mesh.Vertices.AsSpan(i * 4, 4);
                    Check(Math.Abs(quad[1].TexCoord.X - quad[0].TexCoord.X - .25f) < 1e-6f,
                        "torch samples one atlas column");
                    Check(Math.Abs(quad[0].TexCoord.Y - quad[3].TexCoord.Y - .25f) < 1e-6f,
                        "torch samples one atlas row");
                    var cell = quad[3].TexCoord;
                    liveCells.Add((int)(cell.X * 4) + (int)(cell.Y * 4) * 4);
                    rotated |= Math.Abs(quad[1].Normal.Y - quad[0].Normal.Y) > .1f;
                }
                Check(liveCells.Count > 1 && rotated, "torch age animation and birth rotation");
                var flare = scene.Surfaces[^1];
                Check(flare.TextureName == definition.LensFlareTextureName &&
                      flare.Color == new Vector4(1f, 234f / 255f, 59f / 255f, 128f / 255f),
                    "torch lens flare texture and color");
                var flareQuad = scene.Mesh.Vertices.AsSpan(scene.Mesh.Vertices.Length - 4, 4);
                Check(Math.Abs(flareQuad[1].Normal.X - flareQuad[0].Normal.X - 40) < 1e-6f,
                    "torch lens flare diameter");
                Console.WriteLine($"PASS torch atlas: {liveCells.Count} live age cells, rotated billboards");
            }
            if (definition.PointCount > 0)
            {
                NativeQuadVerification.Run(scene, definition);
                var vertices = scene.Mesh.Vertices;
                var coloredSurfaces = scene.Surfaces.Where(
                    surface => surface.TextureMode == ParticleTextureMode.NativeModelColored).ToArray();
                var firstChainVertex = scene.Mesh.Indices[coloredSurfaces[0].IndexStart];
                // Streak point zero is rendered at the current helper while its
                // endpoint-exclusive native solver state remains behind it.
                for (var i = wing ? 2 : 1; i < definition.PointCount; i++)
                    Check(Math.Abs(Vector3.Distance(vertices[firstChainVertex + i * 4].Position,
                        vertices[firstChainVertex + (i - 1) * 4].Position) - definition.LinkLength) < .001f,
                        $"{id} link {i}");
                var movingModel = new SceneModel("moving", model.Mesh!, Vector3.Zero, Vector3.Zero,
                    2, equipmentEffects: scene);
                for (var frame = 0; frame < 30; frame++)
                    scene.ApplyPose(pose, 1f / 60);
                var headVertex = firstChainVertex;
                var retainedVertex = firstChainVertex + 20 * 4;
                var headBeforeMove = Vector3.Transform(vertices[headVertex].Position, movingModel.Transform);
                var retainedBeforeMove = Vector3.Transform(vertices[retainedVertex].Position, movingModel.Transform);
                var relativeBeforeMove = retainedBeforeMove - headBeforeMove;
                var movement = new Vector3(30, -15, 0);
                movingModel.SetPose(movement, Vector3.Zero,
                    new Vector2(movement.X, movement.Y), 0);
                scene.ApplyPose(pose, 0);
                var headAfterMove = Vector3.Transform(vertices[headVertex].Position, movingModel.Transform);
                var retainedAfterMove = Vector3.Transform(vertices[retainedVertex].Position, movingModel.Transform);
                var relativeAfterMove = retainedAfterMove - headAfterMove;
                Check(Vector3.Distance(headAfterMove, headBeforeMove + movement) < .001f,
                    $"{id} chain head follows equipment movement without flicker");
                Check(Vector3.Distance(retainedAfterMove, retainedBeforeMove) < .001f,
                    $"{id} retained chain point stays behind in world space");
                Check(Vector3.Distance(relativeAfterMove, relativeBeforeMove - movement) < .001f,
                    $"{id} equipment movement pushes trail backward");
                // A cached asset selected again gets a reset scene instance.
                _ = new SceneModel("reselected", model.Mesh!, new Vector3(4000, 5000, 0), Vector3.Zero,
                    2, equipmentEffects: scene);
                scene.ApplyPose(pose, 0);
                var expectedTail = wing ? Vector3.Zero : new Vector3(0, -49 * definition.LinkLength, 0);
                Check(Vector3.Distance(vertices[firstChainVertex + 196].Position, expectedTail) < .001f,
                    $"{id} cached trail restores native constructor state");
            }
            if (wing)
            {
                var anchors = model.Diagnostics!.Slices[1].Bones.Where(b => b.Name.StartsWith("fx_streak")).ToArray();
                Check(anchors.Length == 7, "seven native wing emitters");
                Check(anchors.All(b => b.AnimationBoneName == "Bip01 Spine2" && b.Position.Z > 40), "wing helper retargeting");
                var streakCenter = model.Diagnostics.Slices[1].Bones.Single(
                    bone => bone.Name.Equals("stdfx_bone01", StringComparison.OrdinalIgnoreCase));
                Check(anchors.All(anchor => Vector3.Dot(
                        Vector3.Normalize(anchor.Direction),
                        Vector3.Normalize(anchor.Position - streakCenter.Position)) > .9f),
                    "reflected wing helpers retain their authored outward rotation");
                var coloredSurfaces = scene.Surfaces.Where(
                    surface => surface.TextureMode == ParticleTextureMode.NativeModelColored).ToArray();
                int FirstVertex(int surfaceIndex) => scene.Mesh.Indices[coloredSurfaces[surfaceIndex].IndexStart];
                scene.ApplyPose(pose, 1f / 60);
                for (var i = 0; i < anchors.Length; i++)
                {
                    pose.TryTransformRigidPoint(anchors[i].AnimationBoneName!, anchors[i].Position, out var emitter);
                    Check(Vector3.Distance(scene.Mesh.Vertices[FirstVertex(i * 2)].Position, emitter) < .001f,
                        $"wing core head {i} remains attached to animated helper");
                    Check(Vector3.Distance(scene.Mesh.Vertices[FirstVertex(i * 2 + 1)].Position, emitter) < .001f,
                        $"wing halo head {i} remains attached to animated helper");
                }

                var walk = await models.LoadCharacterAnimationAsync(
                    baseName, CharacterMotionKind.Walk, CharacterMotionWeaponStyle.OneHanded);
                Check(walk is not null, "real wing walk animation");
                scene.ResetNativeEffects();
                pose.SetAnimation(walk!);
                pose.Apply(0);
                scene.ApplyPose(pose, 0);
                var poseMovementVertex = FirstVertex(0) + 20 * 4;
                var retainedBeforePoseMovement = scene.Mesh.Vertices[poseMovementVertex].Position;
                pose.Apply(.5f);
                scene.ApplyPose(pose, 0);
                Check(Vector3.Distance(
                          scene.Mesh.Vertices[poseMovementVertex].Position,
                          retainedBeforePoseMovement) < .001f,
                    "wing animation leaves retained trail history in model space");
                Check(pose.TryTransformRigidPoint(
                          anchors[0].AnimationBoneName!, anchors[0].Position, out var animatedEmitter) &&
                      Vector3.Distance(scene.Mesh.Vertices[FirstVertex(0)].Position, animatedEmitter) < .001f,
                    "wing animation keeps chain head on animated emitter");

                scene.ResetNativeEffects();
                var previousHead = Vector3.Zero;
                var previousShape = Vector3.Zero;
                var maximumHeadMovement = 0f;
                var maximumShapeChange = 0f;
                for (var frame = 0; frame < 120; frame++)
                {
                    pose.Apply(frame / 60f);
                    scene.ApplyPose(pose, 1f / 60);
                    var head = scene.Mesh.Vertices[FirstVertex(0)].Position;
                    var history = scene.Mesh.Vertices[FirstVertex(0) + 20 * 4].Position;
                    var shape = history - head;
                    if (frame > 0)
                    {
                        maximumHeadMovement = Math.Max(maximumHeadMovement,
                            Vector3.Distance(head, previousHead));
                        maximumShapeChange = Math.Max(maximumShapeChange,
                            Vector3.Distance(shape, previousShape));
                    }
                    previousHead = head;
                    previousShape = shape;
                }
                Check(maximumHeadMovement > .01f, "wing emitter follows animated spine");
                Check(maximumShapeChange > .01f, "wing trail deforms under animation");

                // Isolate player/camera translation from skeletal animation. The
                // pose is frozen while the model follows a moving world center.
                pose.Apply(0);
                scene.ResetNativeEffects();
                var stationaryModel = new SceneModel("stationary wing", model.Mesh!, Vector3.Zero, Vector3.Zero,
                    2, equipmentEffects: scene);
                scene.ApplyPose(pose, 0);
                for (var frame = 1; frame <= 120; frame++)
                {
                    pose.Apply(0);
                    scene.ApplyPose(pose, 1f / 60);
                }
                var stationaryHead = scene.Mesh.Vertices[FirstVertex(0)].Position;
                var stationaryTail = scene.Mesh.Vertices[FirstVertex(0) + 49 * 4].Position;
                var stationaryShape = stationaryTail - stationaryHead;

                scene.ResetNativeEffects();
                var motionModel = new SceneModel("wing motion", model.Mesh!, Vector3.Zero, Vector3.Zero,
                    2, equipmentEffects: scene);
                scene.ApplyPose(pose, 0);
                const float worldStep = .2f;
                for (var frame = 1; frame <= 120; frame++)
                {
                    var worldPosition = new Vector3(frame * worldStep, 0, 0);
                    motionModel.SetPoseFollowingCamera(
                        worldPosition,
                        Vector3.Zero,
                        new Vector2(worldPosition.X, worldPosition.Y),
                        0,
                        new Vector2(worldStep, 0));
                    pose.Apply(0);
                    scene.ApplyPose(pose, 1f / 60);
                }
                var translationHead = scene.Mesh.Vertices[FirstVertex(0)].Position;
                var translationTail = scene.Mesh.Vertices[FirstVertex(0) + 49 * 4].Position;
                var translationShape = translationTail - translationHead;
                var backwardLocalDistance = stationaryShape.X - translationShape.X;
                Check(backwardLocalDistance > 1,
                    "player/camera movement leaves the frozen-pose wing trail behind");
                Console.WriteLine($"PASS wing player/camera wake {backwardLocalDistance:F3} local units " +
                                  $"(stationary {stationaryShape.X:F3}, moving {translationShape.X:F3})");

                // A modern uncapped render loop can supply less than one native
                // 200 Hz step per frame. Every emitter must still leave the
                // constructor origin and remain attached to its own helper.
                scene.ResetNativeEffects();
                for (var frame = 0; frame < 360; frame++)
                {
                    pose.Apply(frame / 360f);
                    scene.ApplyPose(pose, 1f / 360);
                }
                var highRefreshHeads = Enumerable.Range(0, anchors.Length)
                    .Select(i => scene.Mesh.Vertices[FirstVertex(i * 2)].Position).ToArray();
                Check(highRefreshHeads.All(position => position.LengthSquared() > 1),
                    "wing heads remain on animated helpers above 200 FPS");
                Check(highRefreshHeads.DistinctBy(position => (
                        MathF.Round(position.X, 2), MathF.Round(position.Y, 2), MathF.Round(position.Z, 2))).Count() == anchors.Length,
                    "all seven wing helpers remain represented above 200 FPS");
            }
            if (definition.Kind == SacredModelEffectKind.Whip)
            {
                var attack = await models.LoadCharacterAnimationAsync(baseName, CharacterMotionKind.Attack, CharacterMotionWeaponStyle.OneHanded);
                Check(attack is not null, "real whip attack animation");
                pose.SetAnimation(attack!);
                var maxBend = 0f;
                for (var frame = 0; frame < 120; frame++)
                {
                    pose.Apply(frame / 60f);
                    scene.ApplyPose(pose, 1f / 60);
                    var head = scene.Mesh.Vertices[0].Position;
                    var tail = scene.Mesh.Vertices[196].Position;
                    var axis = Vector3.Normalize(tail - head);
                    for (var i = 1; i < 49; i++)
                        maxBend = Math.Max(maxBend, Vector3.Cross(scene.Mesh.Vertices[i * 4].Position - head, axis).Length());
                }
                Check(maxBend > 5, "whip bends during attack instead of remaining a rigid beam");
                Console.WriteLine($"PASS whip attack curvature {maxBend:F3}");
            }
            Console.WriteLine($"PASS {id} {definition.Kind}: {scene.Mesh.Vertices.Length / 4} quads, {scene.Surfaces.Count} surfaces");
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException($"FAILED: {label}");
    }

}
