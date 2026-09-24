using System.Numerics;
using System.Text.Json;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Models;
using Sacred.Core.Pak.Items;
using Sacred.World.Objects;
using Sacred.Granny.Animation;

var game = args.ElementAtOrDefault(0) ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
var output = args.ElementAtOrDefault(1) ?? "docs/_research/doors/evidence.json";
var items = ItemsPakArchive.Load(Path.Combine(game,"pak","Items.pak")).ToArray();
var byId = items.ToDictionary(i => i.ItemIndex);
var sources = Directory.GetFiles(Path.Combine(game,"bin"),"StartCode.bin",SearchOption.AllDirectories)
    .OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)
    .Select(p=>new WorldObjectScriptSource(p,Path.Combine(Path.GetDirectoryName(p)!,"DefPos.bin")))
    .Append(new WorldObjectScriptSource(Path.Combine(game,"bin","sgf.bin"),Path.Combine(game,"bin","NetScript","DefPos.bin"))).ToArray();
var placements = WorldObjectScriptIndex.Load(sources,items).Placements;
using var models = ModelsPakArchive.Load(Path.Combine(game,"pak","models.pak"),Path.Combine(game,"pak","Models.tmp"));
Vector2[] scenes = [new(1715,3397),new(3050,2725),new(3378,2522),new(3425,2583),new(4555,990)];
var evidence = new List<object>();
foreach(var scene in scenes)
{
    foreach(var p in placements.Where(p=>byId[(ushort)p.TypeId].ModelDesc.Category==SacredItemCategory.Door && Vector2.Distance(p.PreciseWorldPosition,scene)<32))
    {
        var item=byId[(ushort)p.TypeId]; var d=item.ModelDesc;
        var model=await models.LoadModelAsync(item.ModelName);
        if (args.Contains("--bones"))
        {
            foreach(var bone in model.Skin?.Skeleton.Bones ?? [])
                Console.WriteLine($"{item.ItemIndex} bind {bone.Name} parent={bone.ParentIndex} t={bone.RestTranslation} q={bone.RestRotation} scale={bone.RestScaleShear} world={bone.RestWorld}");
        }

        var clips=new List<object>();
        foreach(byte slot in new byte[]{0xAA,0xAB})
        {
            models.TryGetModelMotionName(item.ModelName,slot,out var name);
            var clip=await models.LoadModelAnimationAsync(item.ModelName,slot);
            object? poses=null;
            if(clip is not null && model.Skin is not null && model.Mesh is not null)
            {
                var animated=new GrnAnimatedMesh(model.Mesh,model.Skin,clip);
                animated.ApplyClamped(0); var start=animated.Mesh.Vertices.Select(v=>v.Position).ToArray();
                animated.ApplyClamped(clip.DurationSeconds);
                poses=new { startMin=V(start.Aggregate(Vector3.Min)),startMax=V(start.Aggregate(Vector3.Max)),endMin=V(animated.Mesh.Vertices.Select(v=>v.Position).Aggregate(Vector3.Min)),endMax=V(animated.Mesh.Vertices.Select(v=>v.Position).Aggregate(Vector3.Max)),changed=animated.Mesh.Vertices.Select((v,i)=>Vector3.Distance(v.Position,start[i])).Count(v=>v>.001f)};
            }
            clips.Add(new {slot,name,duration=clip?.DurationSeconds,poses,tracks=clip?.Tracks.Select((t,i)=>new {bone=clip.Skeleton.Bones[i].Name,translationTimes=t?.TranslationTimes,translations=t?.Translations.Select(V),rotationTimes=t?.RotationTimes,rotations=t?.Rotations.Select(q=>new[]{q.X,q.Y,q.Z,q.W})})});
        }
        evidence.Add(new { scene=new[]{scene.X,scene.Y},p.ScriptOffset,p.TypeId,position=new[]{p.PreciseWorldPosition.X,p.PreciseWorldPosition.Y},tileAnchor=new[]{p.WorldX,p.WorldY},p.WorldZ,item.ModelName,d.Angle3D,d.SwitchType,anchor=new[]{d.AnchorX,d.AnchorY},dimensions=new[]{(int)d.Dimensions3D[0],(int)d.Dimensions3D[1],(int)d.Dimensions3D[2]},raw=Convert.ToHexString(Enumerable.Range(0,128).Select(d.GetRawByte).ToArray()),origin=model.Diagnostics is {} diag?V(diag.SourceOriginOffset):null,min=model.Mesh is {} mesh?V(mesh.Vertices.Select(v=>v.Position).Aggregate(Vector3.Min)):null,max=model.Mesh is {} m?V(m.Vertices.Select(v=>v.Position).Aggregate(Vector3.Max)):null,clips});
        Console.WriteLine($"scene {scene}: {p.TypeId} {item.ModelName} at {p.PreciseWorldPosition} angle {d.Angle3D} switch {d.SwitchType}");
    }
}
File.WriteAllText(output,JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
var verification = new List<object>();
foreach (var name in placements.Where(p => scenes.Any(s => Vector2.Distance(p.PreciseWorldPosition, s) < 32) && byId[(ushort)p.TypeId].ModelDesc.Category == SacredItemCategory.Door).Select(p => byId[(ushort)p.TypeId].ModelName).Distinct())
    verification.Add(await DoorVerification.Run(models, name));
File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "verification.json"), JsonSerializer.Serialize(verification, new JsonSerializerOptions { WriteIndented = true }));
static float[] V(Vector3 v)=>[v.X,v.Y,v.Z];





