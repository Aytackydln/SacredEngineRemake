import json, os, sys, time, random, math
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
now = int(time.time())
H = 3600
random.seed(7)
names = ["Thornwick","Lyra Dawnbrook","Grimsby the Odd","Ember Quill","Sir Plinth","Mossback","Vexalia","Dunkelbart","Pip Fennel","Orla Stormreed"]
def own(i): return "demo-hero-%d" % i
def write(mod, d):
    with open(os.path.join(out, mod + ".json"), "w", encoding="utf-8") as f: json.dump(d, f, indent=1)

# ---- server_stats
hours=[]; samples=[]
cur=now//H
for k in range(47,-1,-1):
    h=cur-k
    act=0.3+0.7*max(0,math.sin((h%24-6)/24*6.283))
    hours.append({"h":h,"kills":int(400*act+random.randint(0,60)),"deaths":int(9*act+random.randint(0,3)),"joins":int(6*act+random.randint(0,2)),"chat":int(70*act+random.randint(0,15))})
for k in range(287,-1,-1):
    t=now-k*300; hh=(t//H)%24
    act=max(0,math.sin((hh-6)/24*6.283))
    samples.append({"t":t,"n":max(0,int(1+6*act+random.randint(-1,2)))})
kinds={"Orc Warrior":1840,"Skeleton":1522,"Giant Spider":1207,"Brigand":960,"Wolf":844,"Zombie":702,"Fire Dragon":58,"Troll":401}
write("server_stats",{"state":{"since":now-9*86400,"peak":9,"peak_time":now-5*H},"samples":samples,"hours":hours,"kinds":kinds,
 "total.joins":512,"total.heroes":37,"total.kills":sum(kinds.values()),"total.deaths":143,"total.chat":3921,"total.online":2210000})

# ---- hall_of_fame
write("hall_of_fame",{
 "records":{
  "level":{"name":"Lyra Dawnbrook","uid":own(1),"value":87,"time":now-2*86400},
  "kills":{"name":"Thornwick","uid":own(0),"value":6412,"time":now-3*H},
  "online":{"name":"Mossback","uid":own(5),"value":412000,"time":now-86400},
  "session":{"name":"Mossback","uid":own(5),"value":29400,"time":now-5*86400},
  "biggest":{"name":"Ember Quill","uid":own(3),"value":62,"time":now-4*86400,"extra":"Fire Dragon"},
  "deaths":{"name":"Sir Plinth","uid":own(4),"value":53,"time":now-6*H}},
 "level_firsts":[{"level":10,"name":"Pip Fennel","time":now-9*86400},{"level":25,"name":"Thornwick","time":now-8*86400},{"level":50,"name":"Lyra Dawnbrook","time":now-6*86400}],
 "first_kills":[{"creature":"Fire Dragon","name":"Ember Quill","time":now-4*86400},{"creature":"Troll","name":"Thornwick","time":now-6*86400},{"creature":"Giant Spider","name":"Pip Fennel","time":now-8*86400},{"creature":"Orc Warrior","name":"Pip Fennel","time":now-9*86400},{"creature":"Wolf","name":"Orla Stormreed","time":now-9*86400}],
 "seen_creatures":{"Fire Dragon":True,"Troll":True,"Giant Spider":True,"Orc Warrior":True,"Wolf":True}})

# ---- seasons
def board(rows): return [{"rank":i+1,"name":n,"value":v} for i,(n,v) in enumerate(rows)]
s={"state":{"number":3,"start":now-11*86400},
 "archive":[
  {"number":2,"start":now-41*86400,"finish":now-11*86400,"boards":{"kills":board([("Thornwick",5120),("Orla Stormreed",4310),("Vexalia",3877)]),"gain":board([("Lyra Dawnbrook",41),("Pip Fennel",38)]),"online":board([("Mossback",96.5),("Thornwick",81.2)])}},
  {"number":1,"start":now-71*86400,"finish":now-41*86400,"boards":{"kills":board([("Mossback",3900),("Thornwick",3544)]),"gain":board([("Thornwick",36)]),"online":board([("Mossback",71.0)])}}]}
kills=[3302,2810,2044,1721,1490,1203,880,455]; gains=[22,31,12,9,18,5,27,3]; onl=[61,48,52,33,40,22,30,12]
for j in range(8):
    s["@"+own(j)]={"_name":names[j],"s3.kills":kills[j],"s3.gain":gains[j],"s3.online":onl[j]*3600,"s3.base":10}
write("seasons",s)

# ---- rare_drops
items=[("Dragonbone Greatsword","unique",15,58,2,"Fire Dragon",62,"Ember Quill"),("Gilded Elven Ring","rare",13,41,0,"Troll",44,"Thornwick"),
 ("Ring of the Stoat","rare",12,30,1,"Giant Spider",28,"Pip Fennel"),("Stormcaller's Helm","unique",15,52,3,"Skeleton Lord",50,"Lyra Dawnbrook"),
 ("Warden's Greaves","rare",14,47,0,"Orc Warrior",33,"Orla Stormreed"),("Obsidian Amulet","rare",12,36,0,"Zombie",25,"Vexalia"),
 ("Sunforged Bracers","rare",13,39,2,"Brigand",29,"Thornwick")]
feed=[{"time":now-(i*47+3)*60,"item":it[0],"type":1000+i,"level":it[3],"rarity":it[2],"tier":it[1],"sockets":it[4],"by":it[7],"from":it[5],"from_level":it[6]} for i,it in enumerate(items)]
rd={"feed":feed}
for i,c in [(3,9),(0,7),(1,5),(8,4),(6,3),(9,2)]: rd["@"+own(i)]={"_name":names[i],"rare":c}
write("rare_drops",rd)

# ---- graveyard
dl=[("Sir Plinth",34,"Orc Warrior","creature",31),("Pip Fennel",21,"Giant Spider","creature",24),("Vexalia",58,"Fire Dragon","creature",60),
 ("Dunkelbart",40,None,"unknown",None),("Grimsby the Odd",12,"Thornwick","hero",None),("Sir Plinth",35,"Troll","creature",38),("Mossback",66,"Skeleton Lord","creature",64),
 ("Orla Stormreed",29,"Brigand","creature",27)]
deaths=[]
for i,d in enumerate(dl):
    e={"time":now-(i*41+5)*60,"name":d[0],"level":d[1],"by":d[2] or "unknown","kind":d[3]}
    if d[4]: e["by_level"]=d[4]
    deaths.append(e)
gv={"deaths":deaths,"killers":{"Orc Warrior":19,"Giant Spider":14,"Fire Dragon":11,"Troll":9,"Skeleton Lord":7,"Brigand":6,"Thornwick":2}}
for i,c in [(4,12),(8,5),(6,4),(5,3),(9,3)]: gv["@"+own(i)]={"_name":names[i],"deaths":c}
write("graveyard",gv)

# ---- bounty_board
def bnt(id,def_,title,names_,target,reward,left,players,min_level=1):
    return {"id":id,"def":def_,"title":title,"names":names_,"target":target,"min_level":min_level,"reward":reward,"posted":now-3600,"ends":now+left,"players":players}
bb={"state":{"seq":12,"board":[
 bnt(10,"orcs","Orc slayer",["orc"],32,{"gold":1500},2*H+14*60,{own(0):{"n":14,"name":names[0]},own(1):{"n":32,"name":names[1],"done":True},own(7):{"n":6,"name":names[7]}}),
 bnt(11,"spiders","Web cleaner",["spider"],22,{"gold":1000,"item":{"quality":3}},H+40*60,{own(2):{"n":9,"name":names[2]},own(0):{"n":3,"name":names[0]}}),
 bnt(12,"dragons","Dragon hunter",["dragon"],4,{"gold":8000,"item":{"quality":6}},2*H+55*60,{own(3):{"n":1,"name":names[3]}},20)],
 "history":[{"kind":"done","title":"Grave watch","hero":names[1],"time":now-25*60},{"kind":"expired","title":"Road safety","time":now-70*60,"claims":4},{"kind":"done","title":"Road safety","hero":names[0],"time":now-95*60}]}}
for i,c in [(0,9),(1,8),(7,5),(2,3),(5,2)]: bb["@"+own(i)]={"_name":names[i],"bounties":c}
write("bounty_board",bb)

# ---- achievements
def u(ids,t0): return {i:t0+k*7200 for k,i in enumerate(ids)}
K=["kills_1","kills_100","kills_1000","level_10","level_25","level_50","species_10","species_50","giants_1","deaths_1","deaths_10","online_1h","online_10h","days_3","days_14","streak_3","streak_7"]
rows=[(0,K,61,6412,9,12,25),(1,K[:15]+["pvp_1"],87,3100,6,8,21),(2,K[:11],44,1500,3,3,9),(3,K[:9]+["giants_25"],52,1210,4,5,8),(5,K[:14],66,2400,12,12,40),(7,K[:7],24,420,2,2,4),(8,K[:4],12,120,1,1,2)]
roster={}
for i,ids,lvl,kl,st,best,days in rows:
    uu=u(ids,now-5*86400); roster[own(i)]={"name":names[i],"level":lvl,"kills":kl,"streak":st,"best":best,"days":days,"u":uu,"seen":now-i*3600,"count":len(uu)}
write("achievements",{"roster":roster})

# ---- build_sharer
def bd(id,i,cls,lvl,title,note,tags,verified,skills=(),equip=(),carried=()):
    return {"id":id,"owner":own(i),"name":names[i],"class":cls,"level":lvl,"title":title,"note":note,"tags":tags,"verified":verified,"created":now-3*86400,"updated":now-(i+1)*5400,
       "skills":[{"id":100+k,"level":s[1],"bonus":s[2],"name":s[0]} for k,s in enumerate(skills)],"arts":[],
       "equipment":[{"slot":k,"level":e[1],"tier":e[2],"sockets":e[3],"name":e[0]} for k,e in enumerate(equip)],"carried":list(carried)}
bs={"next_id":5,
 "build:b1":bd("b1",1,1,87,"Smite and shield","Seraphim, hard melee.",["melee","pvm","tank"],True,
    [("Sword Fighting",24,6),("Shield Mastery",18,4),("Hardiness",20,3)],[("Dragonbone Greatsword",58,"unique",2),("Warden's Greaves",47,"rare",0),("Sunforged Bracers",39,"rare",2)]),
 "build:b2":bd("b2",3,3,52,"Fire mage, glass cannon","Fireball chains; needs a tank.",["caster","fire"],True,
    [("Fire Magic",28,7),("Concentration",16,2)],[("Stormcaller's Helm",52,"unique",3),("Obsidian Amulet",36,"rare",0)]),
 "build:b3":bd("b3",0,5,61,"Bow and a dream","Wood Elf, ranged, poison arrows.",["ranged","poison"],True,
    [("Archery",26,5),("Poison Arrows",19,3)],[("Gilded Elven Ring",41,"rare",0)]),
 "build:b4":bd("b4",7,8,24,"Dwarf with hammer","Reported by the player, the server has no hero data.",["early","hammer"],False,carried=["Iron Hammer","Healing Potion x5"]),
}
write("build_sharer",bs)

# ---- quest_helper (fictional catalog and keys)
catalog={"1001":{"name":"Demo quest: The Missing Lantern","area":"Demo Hollow","hint":"Talk to the lamplighter by the east gate (fictional example).","states":{"1":"started","2":"done"}},
 "1002":{"name":"Demo quest: Rat Problem","area":"Demo Cellar","hint":"Clear the cellar below the tavern (fictional example).","states":{"1":"started","2":"done"}},
 "1003":{"name":"Demo quest: Letter for the Mayor","area":"Demo Town","hint":"Deliver the sealed letter (fictional example).","states":{"1":"started"}}}
qh={"catalog":catalog,"known":{"1001":True,"1002":True,"1003":True,"2044":True,"2051":True,"3310":True}}
cnt={"1001":[0,1,2,3,5],"1002":[0,1,2],"1003":[1,3],"2044":[0,1,2,3,5,7],"2051":[0,2],"3310":[1]}
for k,hs in cnt.items():
    for i in hs:
        d=qh.setdefault("@"+own(i),{"_name":names[i]}); d["q."+k]=random.choice([1,2])
write("quest_helper",qh)
print("seeded",out)
