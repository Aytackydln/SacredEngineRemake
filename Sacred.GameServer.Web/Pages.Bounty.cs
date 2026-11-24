using System.Text;

namespace Sacred.GameServer.Web;

internal static partial class Pages
{
    private const string BountyCss = """
.bo{display:grid;gap:1rem;max-width:72rem}.bo label{display:grid;grid-template-columns:13rem 1fr;gap:.5rem;align-items:center;margin:.35rem 0}
.bo label small{display:block;color:var(--dim)}.bo input[type=checkbox]{justify-self:start}.bo .general{max-width:44rem}
.bo table{border-collapse:collapse;width:100%}.bo th,.bo td{text-align:left;padding:.25rem .3rem;border-bottom:1px solid var(--line,#3b3025);vertical-align:top}
.bo th{font-weight:600;font-size:.8rem;color:var(--dim)}.bo td input{width:100%;min-width:3rem;box-sizing:border-box}.bo td input.bo-n{width:4.5rem}.bo td input.bo-t{min-width:9rem}.bo td input.bo-i{min-width:10rem}.bo td input.bo-t{min-width:13rem}.bo td select{min-width:8rem}
.bo .bo-chips{display:flex;flex-wrap:wrap;gap:.25rem;margin-bottom:.25rem}.bo .bo-chip{padding:.1rem .45rem;font-size:.8rem}.bo td select{max-width:11rem}
.bo .status{min-height:1.2rem}.bo .status.bad{color:var(--bad)}.bo .status.ok{color:var(--ok)}.bo .bo-extra{font-size:.75rem;color:var(--dim);margin-top:.15rem}
""";

    private const string BountyScript = """
(function(){'use strict';
var MOD='bounty_board',$=function(s){return document.querySelector(s)},cfg=null;
function status(t,c){var e=$('#status');e.textContent=t||'';e.className='status '+(c||'')}
function api(url,o){return fetch(url,o).then(function(r){return r.text().then(function(t){var j=null;try{j=JSON.parse(t)}catch(e){}if(!r.ok)throw new Error((j&&j.error)||t||('HTTP '+r.status));return j})})}
var GENERAL=[['slots','num','Bounties at once','1 to 20'],['duration','hours','Lifetime (hours)','default lifetime of a bounty; an entry can set its own'],
['announce','bool','Announce','broadcast new bounties and completions'],['history','num','History','completions and expiries kept for the dashboard'],
['max_hunters','num','Hunters per bounty','heroes remembered per bounty']];
var GROUPS=[['Orcs',['orc']],['Undead',['skeleton','zombie','mummy','ghost']],['Spiders',['spider']],['Thieves and pirates',['thief','pirate']],['Wolves, bears and boars',['wolf','bear','boar']],
['Trolls and ogres',['troll','ogre','minotaur']],['Demons and golems',['demon','gargoyle','golem']],['Dragons',['dragon']],['Scorpions and snakes',['scorpion','snake']]];
var WORDS=['bear','boar','bull','demon','dragon','gargoyle','ghost','golem','lizard','minotaur','mummy','ogre','orc','pirate','scorpion','skeleton','snake','spider','thief','troll','wolf','zombie'];
var QUALITIES=[['','No item']];for(var q=0;q<=14;q++)QUALITIES.push([String(q),q===0?'0 - plain':q+(q>=12?' - rare':q>=5?' - magic':' - good')]);
var CATEGORIES=[['','Any item'],['weapon','Weapon'],['armor','Chest armor'],['helmet','Helmet'],['shield','Shield'],['boots','Boots'],['belt','Belt'],['gloves','Gloves'],['arms','Arm guards'],['legs','Leg armor'],['shoulders','Shoulders'],['ring','Ring'],['amulet','Amulet']];
function cap(w){return w.charAt(0).toUpperCase()+w.slice(1)}
var COLS=[['id','text','Id','bo-i'],['title','text','Title','bo-t'],['names','chips','Creatures',''],['tmin','num','From','bo-n'],['tmax','num','To','bo-n'],['min_level','num','Min level','bo-n'],
['hours','num','Hours','bo-n'],['gold','num','Gold','bo-n'],['quality','select','Item quality',''],['category','select','Item category','']];
function el(t,c,x){var e=document.createElement(t);if(c)e.className=c;if(x!=null)e.textContent=x;return e}
function build(){
 var g=$('#general');g.textContent='';
 GENERAL.forEach(function(d){var l=el('label'),s=el('span',null,d[2]),i=el('input');s.appendChild(el('small',null,d[3]));i.id='g_'+d[0];
  var v=cfg[d[0]];if(d[1]==='bool'){i.type='checkbox';i.checked=!!v}else{i.type='text';i.value=d[1]==='hours'?(v/3600):v}l.appendChild(s);l.appendChild(i);g.appendChild(l)});
 var body=$('#pool');body.textContent='';
 cfg.pool.forEach(function(e,idx){body.appendChild(row(e,idx))});
}
function row(e,idx){
 var tr=el('tr');tr.dataset.i=idx;
 var t=Array.isArray(e.target)?e.target:[e.target,e.target],r=e.reward||{},it=r.item||{};
 var v={id:e.id,title:e.title||'',names:(e.names||[]).join(', '),tmin:t[0],tmax:t[1]==null?t[0]:t[1],min_level:e.min_level||'',hours:e.duration?e.duration/3600:'',gold:r.gold||'',quality:it.quality==null?'':it.quality,category:it.category||''};
 COLS.forEach(function(c){var td=el('td');
  if(c[0]==='names'){tr._names=(e.names||[]).slice();td.appendChild(chips(tr));
   if(e.types||e.sectors){var x=[];if(e.types)x.push('types '+e.types.join(','));if(e.sectors)x.push('sectors '+e.sectors.join(',')+(e.region?' ('+e.region+')':''));td.appendChild(el('div','bo-extra',x.join('; ')))}
   tr.appendChild(td);return}
  if(c[1]==='select'){var sl=el('select');sl.dataset.k=c[0];sl.setAttribute('aria-label',c[2]);
   (c[0]==='quality'?QUALITIES:CATEGORIES).forEach(function(o){var op=el('option',null,o[1]);op.value=o[0];sl.appendChild(op)});
   var cur=v[c[0]];sl.value=String(cur==null?'':cur);if(sl.value!==String(cur==null?'':cur)){var ex=el('option',null,String(cur));ex.value=String(cur);sl.appendChild(ex);sl.value=String(cur)}
   td.appendChild(sl);tr.appendChild(td);return}
  var i=el('input',c[3]);i.type='text';i.dataset.k=c[0];i.value=v[c[0]]==null?'':v[c[0]];i.setAttribute('aria-label',c[2]);td.appendChild(i);
  tr.appendChild(td)});
 var td=el('td'),b=el('button','ghost','Delete');b.type='button';b.onclick=function(){tr.remove()};td.appendChild(b);tr.appendChild(td);return tr}
function chips(tr){
 var box=el('div'),list=el('div','bo-chips'),sel=el('select');box.appendChild(list);
 function draw(){list.textContent='';if(!tr._names.length)list.appendChild(el('span','dim small','needs a creature or types'));
  tr._names.forEach(function(w,k){var b=el('button','ghost bo-chip',cap(w)+' ×');b.type='button';b.title='Remove';b.onclick=function(){tr._names.splice(k,1);draw()};list.appendChild(b)})}
 function opt(p,t,v){var o=el('option',null,t);o.value=v;p.appendChild(o)}
 opt(sel,'+ Add a creature...','');var g=el('optgroup');g.label='Groups';GROUPS.forEach(function(x,k){opt(g,x[0],'g'+k)});sel.appendChild(g);
 var w=el('optgroup');w.label='Creatures';WORDS.forEach(function(x){opt(w,cap(x),'w'+x)});sel.appendChild(w);opt(sel,'Other word...','o');
 sel.onchange=function(){var v=sel.value;sel.value='';var add=[];
  if(v[0]==='g')add=GROUPS[+v.slice(1)][1];else if(v[0]==='w')add=[v.slice(1)];
  else if(v==='o'){var t=prompt('Word of the creature name (e.g. goblin):');if(t)add=t.toLowerCase().split(/[^a-z]+/).filter(Boolean)}
  add.forEach(function(x){if(tr._names.indexOf(x)<0)tr._names.push(x)});draw()};
 box.appendChild(sel);draw();return box}
function num(s){var n=parseFloat(String(s).replace(',','.'));return isNaN(n)?null:n}
function collect(){
 var out=JSON.parse(JSON.stringify(cfg)),bad=null;
 GENERAL.forEach(function(d){var e=$('#g_'+d[0]);if(d[1]==='bool'){out[d[0]]=e.checked;return}var n=num(e.value);if(n==null){bad=d[2]+' is not a number';return}out[d[0]]=d[1]==='hours'?Math.round(n*3600):n});
 var pool=[];
 document.querySelectorAll('#pool tr').forEach(function(tr){
  var orig=cfg.pool[+tr.dataset.i]||{},e=JSON.parse(JSON.stringify(orig)),f={};
  tr.querySelectorAll('input,select').forEach(function(i){f[i.dataset.k]=i.value.trim()});
  if(!f.id){bad='every bounty needs an id';return}
  e.id=f.id;e.title=f.title||f.id;
  var names=tr._names.slice();if(names.length)e.names=names;else delete e.names;
  if(!e.names&&!e.types){bad=f.id+': creature words are needed';return}
  var a=num(f.tmin),b=num(f.tmax);if(a==null){bad=f.id+': needs a target';return}if(b==null)b=a;e.target=a===b?a:[a,Math.max(a,b)];
  var ml=num(f.min_level);if(ml)e.min_level=ml;else delete e.min_level;
  var h=num(f.hours);if(h)e.duration=Math.round(h*3600);else delete e.duration;
  var reward={},gold=num(f.gold);if(gold)reward.gold=gold;
  var q=num(f.quality);if(q!=null||f.category){var item={};if(q!=null)item.quality=q;if(f.category)item.category=f.category;reward.item=item}
  e.reward=reward;pool.push(e)});
 if(!bad&&pool.length===0)bad='the pool needs at least one bounty';
 out.pool=pool;return {cfg:out,bad:bad}}
function load(){return api('mods/'+MOD+'/rules').then(function(r){cfg=r;$('#missing').hidden=true;$('#main').hidden=false;build()}).catch(function(e){$('#main').hidden=true;$('#missing').hidden=false;$('#why').textContent=e.message})}
$('#add').onclick=function(){var e={id:'new-'+(document.querySelectorAll('#pool tr').length+1),title:'New bounty',names:['orc'],target:[10,20],reward:{gold:1000}};cfg.pool.push(e);$('#pool').appendChild(row(e,cfg.pool.length-1))};
$('#save').onclick=function(){var c=collect();if(c.bad){status(c.bad,'bad');return}
 api('api/mod-store',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mod:MOD,set:{config:c.cfg}})}).then(function(){status('Saved. Applies within 2 seconds, no reload. Bounties already posted keep their values.','ok');setTimeout(function(){load()},2500)}).catch(function(e){status(e.message,'bad')})};
$('#reload').onclick=function(){load().then(function(){status('')})};
load();
})();
""";

    /// <summary>
    /// <c>GET /bounties</c>: the bounty board's configuration (slots, lifetime, announcements and the pool of bounties: creature words, target range,
    /// minimum level, lifetime, gold and item reward) edited while the server runs: reads the mod's admin-only <c>rules</c> endpoint and writes its storage
    /// key <c>config</c> through <c>POST /api/mod-store</c>. Pool fields the page has no column for (<c>types</c>, <c>sectors</c>, <c>region</c>) are kept.
    /// </summary>
    public static string BountyPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(8 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\"><script>try{var m=localStorage.getItem('gsTheme');if(m==='light'||m==='dark')document.documentElement.dataset.theme=m;}catch(e){}</script><title>Bounties - ")
          .Append(E(o.Name)).Append(" - Sacred GameServer</title><style>").Append(Style).Append(BountyCss).Append("</style></head><body><div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "bounties", admin.Mods().Count);
        sb.Append("</header><main class=\"bo\"><section class=\"card\" id=\"missing\" hidden><h2>Bounty board</h2><p>The <code>bounty_board</code> mod is not loaded (or too old for this page). Copy <code>bounty_board.lua</code> from <code>scripts-examples</code> into the scripts folder.</p><p class=\"dim small\" id=\"why\"></p></section>")
          .Append("<div id=\"main\" hidden><section class=\"card general\"><h2>Bounty board</h2><div id=\"general\"></div></section>")
          .Append("<section class=\"card\"><h2>Bounties</h2><p class=\"dim small\">The board draws from this list, never the same entry twice at once. Creatures match whole words of a creature's name (needs <code>--game-dir</code>); pick a group or a creature, or add another word; the target is drawn between From and To. ")
          .Append("Item quality: 0 is plain, a higher number is rarer; \"No item\" pays gold only. The category narrows the item (any item by default). Hours empty: the default lifetime.</p>")
          .Append("<div style=\"overflow-x:auto\"><table><thead><tr><th>Id</th><th>Title</th><th>Creatures</th><th>From</th><th>To</th><th>Min level</th><th>Hours</th><th>Gold</th><th>Item quality</th><th>Item category</th><th></th></tr></thead><tbody id=\"pool\"></tbody></table></div>")
          .Append("<div class=\"row\" style=\"margin-top:.8rem\"><button type=\"button\" class=\"ghost\" id=\"add\">Add bounty</button><button type=\"button\" class=\"primary\" id=\"save\">Save</button><button type=\"button\" class=\"ghost\" id=\"reload\">Refresh</button></div>")
          .Append("<p class=\"hint dim small\">Stored in the mod's storage (key <code>config</code>); a change applies within 2 seconds, no restart or reload. Bounties already on the board keep their values; the script file's values only seed a fresh install.</p>")
          .Append("<p class=\"status\" id=\"status\" aria-live=\"polite\"></p></section></div></main></div><script>").Append(BountyScript).Append("</script></body></html>");
        return sb.ToString();
    }
}
