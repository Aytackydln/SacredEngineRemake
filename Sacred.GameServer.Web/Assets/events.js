(function(){'use strict';
var MOD='world_events', $=function(s){return document.querySelector(s)};
var data=null, edit=null, pendingControl=0;
function esc(t){return String(t==null?'':t).replace(/[&<>"']/g,function(c){return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]})}
function status(t,cls){var e=$('#status');e.textContent=t||'';e.className='status '+(cls||'')}
function api(url,opts){return fetch(url,opts).then(function(r){return r.text().then(function(t){var j=null;try{j=JSON.parse(t)}catch(e){}
  if(!r.ok)throw new Error((j&&j.error)||t||('HTTP '+r.status));return j})})}
function post(set){return api('api/mod-store',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mod:MOD,set:set})})}
function dur(s){s=Math.round(s);if(s>=3600)return Math.floor(s/3600)+'h '+('0'+Math.floor(s%3600/60)).slice(-2)+'m';if(s>=60)return Math.floor(s/60)+'m';return s+'s'}
function num(v){var n=parseFloat(v);return isNaN(n)?undefined:n}
function load(){return api('mods/'+MOD+'/events').then(function(d){data=d;render();$('#missing').hidden=true;$('#main').hidden=false;})
  .catch(function(e){$('#main').hidden=true;$('#missing').hidden=false;$('#missingwhy').textContent=e.message})}

function render(){
  var r=data.running, h='';
  if(r){h='<b>'+esc(r.title)+'</b> is running: '+r.count+' / '+r.target+' ('+Math.min(100,Math.floor(r.count*100/r.target))+'%), '+esc(dur(r.left))+' left'
    +'<div class="bar"><i style="width:'+Math.min(100,r.count*100/r.target)+'%"></i></div><button type="button" class="bad" id="cancel">Cancel it (no rewards)</button>';}
  else h='No event is running. The next one starts in about '+esc(dur(data.next_in||0))+'. <button type="button" class="ghost" id="nextnow">Start the next one now</button>';
  if(data.control)h+='<p class="hint dim small">Last command: '+esc(data.control.message)+(data.control.ok?'':' (refused)')+'</p>';
  $('#runbox').innerHTML=h;
  var rows='';
  data.events.forEach(function(e,i){
    var d=e.def||{}, src=e.source==='file'?'script file (legacy)':e.source==='override'?'file event, changed here':'stored';
    rows+='<tr class="'+(e.enabled?'':'off')+'"><td><b>'+esc(d.title||e.id)+'</b><br><small>'+esc(e.id)+' &middot; '+src+'</small>'
      +(e.invalid?'<br><small class="bad" style="color:var(--bad)">refused: '+esc(e.invalid)+'</small>':'')+'</td>'
      +'<td>'+esc(e.goal||'')+'<br><small>'+(d.target||'?')+' kills in '+dur(d.duration||0)+(d.hooks&&d.hooks.length?' &middot; '+d.hooks.length+' hook'+(d.hooks.length>1?'s':''):'')+(d.area||d.areas?' &middot; area '+esc([].concat(d.area||[],d.areas||[]).join(', ')):'')+'</small></td>'
      +'<td>'+(e.running?'<span class="pill ok">running</span>':e.enabled?'<span class="pill">in rotation</span>':'<span class="pill warn">off</span>')+'</td>'
      +'<td class="act"><button type="button" data-a="start" data-i="'+i+'">Start now</button><button type="button" data-a="edit" data-i="'+i+'">Edit</button>'
      +'<button type="button" data-a="toggle" data-i="'+i+'">'+(e.enabled?'Turn off':'Turn on')+'</button>'
      +(e.source==='web'?'<button type="button" class="bad" data-a="del" data-i="'+i+'">Delete</button>':e.source==='override'?'<button type="button" data-a="reset" data-i="'+i+'" title="Back to the script file\'s version">Reset</button>':'')+'</td></tr>';
  });
  $('#events').innerHTML=rows||'<tr><td colspan="4" class="dim">No events yet.</td></tr>';
}

function webMap(){var m={};data.events.forEach(function(e){if((e.source==='web'||e.source==='override')&&!e.invalid)m[e.id]=e.def});return m}
function offMap(){var m={};data.events.forEach(function(e){if(!e.enabled)m[e.id]=true});return m}
function hookCount(){var m={};data.events.forEach(function(e){if(e.def&&e.def.hooks)m[e.id]=e.def.hooks.length});return m}
function persist(what){var sent=hookCount();status('Saving...');return post({events:webMap(),events_off:offMap()}).then(function(){status('Saved. Waiting for the server to pick it up...');
  return new Promise(function(ok){setTimeout(ok,2600)})}).then(load).then(function(){var lost=[];data.events.forEach(function(e){var have=e.def&&e.def.hooks?e.def.hooks.length:0;if(sent[e.id]>have)lost.push(e.id+' ('+(sent[e.id]-have)+')')});
  if(lost.length)status(what+' The server refused some hooks of: '+lost.join(', ')+' (a hook needs a valid trigger and at least one valid action; see the server log).','bad');else status(what+' Done: the server is using it.','ok')}).catch(function(e){status(e.message,'bad')})}
function control(cmd,event){var t=Date.now();status('Sending...');post({control:{cmd:cmd,event:event||'',t:t}}).then(function(){
  return new Promise(function(ok){setTimeout(ok,2600)})}).then(load).then(function(){var c=data.control;status(c?c.message:'Sent.',c&&c.ok?'ok':'bad')}).catch(function(e){status(e.message,'bad')})}

document.addEventListener('click',function(ev){
  var b=ev.target.closest('button');if(!b)return;
  if(b.id==='cancel'){if(confirm('Cancel the running event? Nobody gets a reward.'))control('cancel');return}
  if(b.id==='nextnow'){control('next');return}
  if(b.id==='new'){openEditor(null);return}
  var i=b.dataset.i,a=b.dataset.a;if(a==null)return;
  var e=data.events[+i];
  if(a==='start'){if(confirm('Start "'+(e.def.title||e.id)+'" now?'))control('start',e.id)}
  else if(a==='edit')openEditor(e);
  else if(a==='toggle'){e.enabled=!e.enabled;persist(e.enabled?'Turned on.':'Turned off.')}
  else if(a==='del'){if(confirm('Delete "'+(e.def.title||e.id)+'"? A running copy of it finishes as it is.')){data.events.splice(+i,1);persist('Deleted.')}}
  else if(a==='reset'){if(confirm('Drop your changes and use the script file\'s version again?')){e.source='file';persist('Reset.')}}
});

function openEditor(e){
  edit=e;var d=e?JSON.parse(JSON.stringify(e.def)):{id:'',title:'',target:100,duration:3600,min_contribution:1,reward:{gold:0,gold_each:0}};
  d.reward=d.reward||{};var rw=d.reward, areas=[].concat(d.area||[],d.areas||[]), tiers=data.tiers;
  var ar='';Object.keys(data.areas).sort().forEach(function(id){ar+='<label class="chk"><input type="checkbox" name="area" value="'+esc(id)+'"'+(areas.indexOf(id)>=0?' checked':'')+'> '+esc(data.areas[id])+' <small>'+esc(id)+'</small></label>'});
  var ms=(rw.milestones||[]);
  var rg='';(data.regions||[]).forEach(function(r){rg+='<label class="chk"><input type="checkbox" name="region" value="'+r.id+'"'+((d.regions||[]).indexOf(r.id)>=0?' checked':'')+'> '+esc(r.name)+'</label>'});
  var h='<form method="dialog" id="eform"><h3>'+(e?'Edit event':'New event')+'</h3>'
   +'<div class="two"><label>Id<input name="id" '+(e?'readonly ':'')+'value="'+esc(d.id)+'" maxlength="40" placeholder="goblin-hunt" pattern="[a-z0-9][a-z0-9_\\-]*" required></label>'
   +'<label>Title<input name="title" value="'+esc(d.title)+'" maxlength="60" placeholder="Goblin hunt"></label></div>'
   +'<div class="two"><label>Creatures to defeat<input name="target" type="number" min="1" max="10000000" value="'+esc(d.target)+'" required></label>'
   +'<label>Time (minutes)<input name="duration" type="number" min="1" max="43200" step="any" value="'+esc(Math.round((d.duration||3600)/60*100)/100)+'" required></label>'
   +'<label>Kills needed to share the reward<input name="min_contribution" type="number" min="1" value="'+esc(d.min_contribution||1)+'"></label></div>'
   +'<div class="two"><label>Only creatures named (comma separated words)<input name="names" value="'+esc((d.names||[]).join(', '))+'" placeholder="goblin, orc"></label>'
   +'<label>or creature types (numbers)<input name="types" value="'+esc((d.types||[]).join(', '))+'"></label>'
   +'<label>Counted thing, plural<input name="unit" value="'+esc(d.unit||'')+'" maxlength="30" placeholder="goblins"></label></div>'
   +'<label>Goal in words (optional)<input name="text" value="'+esc(d.text||'')+'" maxlength="200" placeholder="Slay 500 goblins"></label>'
   +'<fieldset><legend>Where (nothing picked: everywhere)</legend>'+(ar||'<small>No areas yet: draw some on the Areas page.</small>')
   +(rg?'<div class="regpick"><small>Game regions (every sector of the region counts; need --game-dir)</small><div>'+rg+'</div></div>':'')
   +'<label style="margin-top:.4rem">A kill counts where<select name="where"><option value="">the default of the script</option><option value="victim">the creature died</option><option value="player">the hero stands</option><option value="either">either</option></select></label></fieldset>'
   +'<fieldset><legend>Reward</legend><div class="two"><label>Gold pool, shared by kills<input name="gold" type="number" min="0" value="'+esc(rw.gold||0)+'"></label>'
   +'<label>Gold for everyone who qualifies<input name="gold_each" type="number" min="0" value="'+esc(rw.gold_each||0)+'"></label></div>'
   +'<div class="two"><label>Item for everyone: quality 0-14 (empty: none)<input name="iq" type="number" min="0" max="14" value="'+esc(rw.item&&rw.item.quality!=null?rw.item.quality:'')+'"></label>'
   +'<label>Item kind (e.g. ring, empty: any)<input name="icat" value="'+esc(rw.item&&rw.item.category||'')+'" maxlength="20"></label>'
   +'<label>Item level (empty: the hero\'s)<input name="ilvl" type="number" min="1" max="200" value="'+esc(rw.item&&rw.item.level!=null?rw.item.level:'')+'"></label>'
   +'<label>Prize for the best: quality 0-14 (empty: none)<input name="tq" type="number" min="0" max="14" value="'+esc(rw.top_item&&rw.top_item.quality!=null?rw.top_item.quality:'')+'"></label></div>'
   +'<p class="hint dim small">Rarity odds by progress (optional): when set, each qualifying hero rolls one item with the odds of the highest row reached; the item kind above still applies.</p>'
   +'<table id="ms"><tr><th>At %</th>'+tiers.map(function(t){return '<th>'+esc(t.name)+'</th>'}).join('')+'<th></th></tr>'+ms.map(function(m){return msRow(m,tiers)}).join('')+'</table>'
   +'<div class="row"><button type="button" id="addms">Add row</button><label class="chk"><input type="checkbox" name="nofail"'+(rw.on_fail===false?' checked':'')+'> no item roll when time runs out</label></div></fieldset>'
   +hooksHtml((d.hooks||[]).map(hookToModel))
   +'<p class="status bad" id="eerr"></p><div class="row" style="justify-content:flex-end"><button type="button" id="ecancel" class="ghost">Cancel</button><button class="primary" id="esave">Save</button></div></form>';
  var dlg=$('#editor');dlg.innerHTML=h;dlg.querySelector('[name=where]').value=d.where||'';
  dlg.querySelector('#ecancel').onclick=function(){dlg.close()};
  dlg.querySelector('#addms').onclick=function(){var t=document.createElement('tbody');t.innerHTML=msRow({at:'',weights:{}},tiers);dlg.querySelector('#ms').appendChild(t.firstChild)};
  dlg.querySelector('#ms').addEventListener('click',function(ev){var b=ev.target.closest('button.rm');if(b)b.closest('tr').remove()});
  wireHooks(dlg);
  dlg.querySelector('#eform').onsubmit=function(ev){ev.preventDefault();saveEditor(dlg,tiers)};
  dlg.showModal();
}
function msRow(m,tiers){return '<tr><td><input class="at" type="number" min="0" max="100" step="any" value="'+esc(m.at)+'"></td>'+tiers.map(function(t){return '<td><input data-t="'+esc(t.id)+'" type="number" min="0" step="any" value="'+esc(m.weights&&m.weights[t.id]!=null?m.weights[t.id]:'')+'"></td>'}).join('')+'<td><button type="button" class="rm ghost" aria-label="Remove row">&times;</button></td></tr>'}
function list(v,conv){return v.split(/[,;\n]/).map(function(s){return s.trim()}).filter(Boolean).map(conv||String)}
function saveEditor(dlg,tiers){
  var f=dlg.querySelector('#eform'), g=function(n){return f.elements[n].value}, err=dlg.querySelector('#eerr');
  var id=g('id').trim().toLowerCase();
  if(!/^[a-z0-9][a-z0-9_-]{0,39}$/.test(id)){err.textContent='The id is 1-40 characters of a-z 0-9 _ -';return}
  if(!edit&&data.events.some(function(e){return e.id===id})){err.textContent='There is already an event with this id; edit it instead.';return}
  var target=num(g('target')), mins=num(g('duration'));
  if(!(target>=1)||!(mins>=1)){err.textContent='Target and time are needed.';return}
  var def={id:id,title:g('title').trim()||id,target:Math.floor(target),duration:Math.round(mins*60),min_contribution:Math.max(1,Math.floor(num(g('min_contribution'))||1))};
  var names=list(g('names')),types=list(g('types'),Number).filter(function(n){return !isNaN(n)});
  if(names.length)def.names=names;if(types.length)def.types=types;
  if(g('unit').trim())def.unit=g('unit').trim();if(g('text').trim())def.text=g('text').trim();
  var areas=[].slice.call(f.querySelectorAll('[name=area]:checked')).map(function(c){return c.value});
  if(areas.length===1)def.area=areas[0];else if(areas.length>1)def.areas=areas;
  var regs=[].slice.call(f.querySelectorAll('[name=region]:checked')).map(function(c){return Number(c.value)});if(regs.length)def.regions=regs;
  if(g('where'))def.where=g('where');
  if(edit&&edit.def.sectors)def.sectors=edit.def.sectors;
  var rw={gold:Math.max(0,Math.floor(num(g('gold'))||0)),gold_each:Math.max(0,Math.floor(num(g('gold_each'))||0))};
  var iq=num(g('iq')),cat=g('icat').trim().toLowerCase(),il=num(g('ilvl'));
  var msRows=[].slice.call(f.querySelectorAll('#ms tr')).slice(1).map(function(tr){var m={at:num(tr.querySelector('.at').value),weights:{}};
    [].forEach.call(tr.querySelectorAll('[data-t]'),function(i){var w=num(i.value);if(w>0)m.weights[i.dataset.t]=w});return m}).filter(function(m){return m.at!=null&&Object.keys(m.weights).length});
  if(iq!=null||cat||il!=null||msRows.length){rw.item={};if(iq!=null)rw.item.quality=iq;if(cat)rw.item.category=cat;if(il!=null)rw.item.level=il}
  var tq=num(g('tq'));if(tq!=null)rw.top_item={quality:tq};
  if(msRows.length)rw.milestones=msRows.sort(function(a,b){return a.at-b.at});
  if(f.elements.nofail.checked)rw.on_fail=false;
  var hk=hooksToDef(readHooks(f));if(typeof hk==='string'){err.textContent=hk;return}
  if(hk.length)def.hooks=hk;
  def.reward=rw;def.created=(edit&&edit.def.created)||Date.now();
  var entry={id:id,source:edit?(edit.source==='file'?'override':edit.source):'web',enabled:edit?edit.enabled:true,def:def};
  if(edit){var at=data.events.indexOf(edit);data.events[at]=entry}else data.events.push(entry);
  dlg.close();persist('Saved "'+def.title+'".');
}

/* ---- hooks: a trigger and a list of actions ------------------------------------------------------------------------------------- */
var TRIG=[['at','Progress reaches (% of the goal)'],['at_time','Time passed (minutes)'],['start','The event starts'],['success','The goal is reached'],['fail','The time runs out'],['kills','Creatures of one kind defeated']];
var ACTS=[['spawn','Spawn creatures'],['item','Drop an item'],['gold','Drop gold'],['message','Send a message']];
var TARGETS=[['killer','the hero who made the kill'],['host','the host'],['random','a random hero in the area'],['owner','the area\'s owner (or the only hero there)'],['players','every hero in the area'],['global','everybody online']];
var DEF_TARGET={spawn:'owner',item:'killer',message:'global',gold:'killer'};
function opts(list,sel){return list.map(function(o){return '<option value="'+esc(o[0])+'"'+(o[0]===sel?' selected':'')+'>'+esc(o[1])+'</option>'}).join('')}
function hookToModel(h){
  var m={trig:'at',v:'',creature:'',count:1,per:false,actions:[]};
  if(h.on)m.trig=h.on;else if(h.kills){m.trig='kills';m.creature=h.kills.creature;m.count=h.kills.count;m.per=!!h.kills.per_player;m.region=h.kills.region||''}
  else if(h.at_time!=null){m.trig='at_time';m.v=Math.round(h.at_time/60*100)/100}else{m.trig='at';m.v=h.at}
  (h.actions||[]).forEach(function(a){
    var x={kind:a.action,target:a.target||DEF_TARGET[a.action],text:a.text||''};
    if(a.action==='spawn'){x.n=a.n||1;x.radius=a.radius||400;x.per=a.per_player!==false;x.kinds=(a.kinds||[]).map(function(k){return{type:k.type,weight:k.weight||1}})}
    else if(a.action==='item'){x.n=a.n||1;var s=a.spec||{};x.type=s.type!=null?s.type:'';x.quality=s.quality!=null?s.quality:'';x.category=s.category||'';x.level=s.level!=null?s.level:''}
    else if(a.action==='gold')x.amount=a.amount||0;
    m.actions.push(x)});
  return m}
function actHtml(a){
  if(a.kind==='spawn'){a.n=a.n||5;a.radius=a.radius||400;if(!a.kinds||!a.kinds.length)a.kinds=[{type:'',weight:1}]}else if(a.kind==='item'){a.n=a.n||1}else if(a.kind==='gold'){a.amount=a.amount||100}
  var h='<div class="act"><div class="row"><label>Then<select class="ak">'+opts(ACTS,a.kind)+'</select></label>'
   +(a.kind==='message'&&a.target==='global'?'':'')+'<label>for<select class="tg">'+opts(TARGETS,a.target)+'</select></label>'
   +'<span class="mv"><button type="button" class="ghost" data-hk="aup" title="Move up" aria-label="Move the action up">&uarr;</button><button type="button" class="ghost" data-hk="adown" title="Move down" aria-label="Move the action down">&darr;</button><button type="button" class="ghost" data-hk="arm" aria-label="Remove the action">&times;</button></span></div>';
  if(a.kind==='spawn'){
    h+='<div class="two"><label>How many<input class="n" type="number" min="1" max="40" value="'+esc(a.n)+'"></label><label>Radius around the hero (world units)<input class="radius" type="number" min="50" max="3000" value="'+esc(a.radius)+'"></label></div>'
     +'<label class="chk"><input type="checkbox" class="per"'+(a.per?' checked':'')+'> that many for each hero (off: shared out among them)</label>'
     +'<div class="kinds"><small>Kinds (the game\'s creature name such as Orc Warrior, ORC_WARRIOR.GRN or a type number) with a weight: a kind with weight 3 is three times as likely as one with 1.</small>'
     +a.kinds.map(function(k){return '<div class="row kind"><input class="kt" placeholder="Orc Warrior" value="'+esc(k.type)+'" maxlength="48" aria-label="Creature"><label class="inl">weight<input class="kw" type="number" min="1" max="1000" value="'+esc(k.weight)+'"></label><button type="button" class="ghost" data-hk="krm" aria-label="Remove the kind">&times;</button></div>'}).join('')
     +'<button type="button" class="ghost" data-hk="kadd">Add a kind</button></div>';
  }else if(a.kind==='item'){
    h+='<div class="two"><label>How many each<input class="n" type="number" min="1" max="10" value="'+esc(a.n)+'"></label><label>Item name (empty: roll by rarity; only the Gear mod in its own game can make a named item)<input class="itype" value="'+esc(a.type)+'" maxlength="48"></label></div>'
     +'<div class="two"><label>Quality 0-14<input class="iq" type="number" min="0" max="14" value="'+esc(a.quality)+'"></label><label>Kind (ring, weapon, ...)<input class="icat" value="'+esc(a.category)+'" maxlength="20"></label><label>Level (empty: the hero\'s)<input class="ilvl" type="number" min="1" max="200" value="'+esc(a.level)+'"></label></div>';
  }else if(a.kind==='gold'){
    h+='<label>Gold<input class="amount" type="number" min="1" max="2000000000" value="'+esc(a.amount)+'"></label>';
  }
  h+='<label>'+(a.kind==='message'?'Message':'Message to the hero (optional)')+'<input class="atext" maxlength="160" value="'+esc(a.text)+'"></label></div>';
  return h}
function hookHtml(m,i){
  var h='<div class="hk"><div class="row hkhead"><b>Hook '+(i+1)+'</b><label>When<select class="tr">'+opts(TRIG,m.trig)+'</select></label>';
  if(m.trig==='at')h+='<label class="inl"><input class="tv" type="number" min="0.1" max="100" step="any" value="'+esc(m.v)+'" aria-label="Percent"> %</label>';
  else if(m.trig==='at_time')h+='<label class="inl"><input class="tv" type="number" min="0.02" max="43200" step="any" value="'+esc(m.v)+'" aria-label="Minutes"> minutes</label>';
  else if(m.trig==='kills')h+='<label class="inl"><input class="tc" value="'+esc(m.creature)+'" placeholder="Orc Shaman" maxlength="48" aria-label="Creature name or type number"></label><label class="inl"><input class="tn" type="number" min="1" max="100000" value="'+esc(m.count)+'" aria-label="How many"> times</label>'
    +'<label class="inl">in<select class="trg"><option value="">anywhere</option>'+(data.regions||[]).map(function(r){return '<option value="'+r.id+'"'+(String(m.region)===String(r.id)?' selected':'')+'>'+esc(r.name)+'</option>'}).join('')+'</select></label>'
    +'<label class="chk"><input type="checkbox" class="tp"'+(m.per?' checked':'')+'> each hero on their own</label>';
  h+='<span class="mv"><button type="button" class="ghost" data-hk="up" title="Move up" aria-label="Move the hook up">&uarr;</button><button type="button" class="ghost" data-hk="down" title="Move down" aria-label="Move the hook down">&darr;</button><button type="button" class="ghost bad" data-hk="rm" aria-label="Remove the hook">&times;</button></span></div>'
   +m.actions.map(actHtml).join('')+'<button type="button" class="ghost" data-hk="aadd">Add an action</button></div>';
  return h}
function readHooks(root){
  return [].map.call(root.querySelectorAll('.hk'),function(hk){
    var q=function(el,s){var e=el.querySelector(s);return e?e.value:''}, c=function(el,s){var e=el.querySelector(s);return !!(e&&e.checked)};
    var m={trig:q(hk,'.tr'),v:q(hk,'.tv'),creature:q(hk,'.tc'),count:q(hk,'.tn'),per:c(hk,'.tp'),region:q(hk,'.trg'),actions:[]};
    [].forEach.call(hk.querySelectorAll('.act'),function(ae){
      var a={kind:q(ae,'.ak'),target:q(ae,'.tg'),text:q(ae,'.atext'),n:q(ae,'.n'),radius:q(ae,'.radius'),per:c(ae,'.per'),amount:q(ae,'.amount'),
        type:q(ae,'.itype'),quality:q(ae,'.iq'),category:q(ae,'.icat'),level:q(ae,'.ilvl'),kinds:[]};
      [].forEach.call(ae.querySelectorAll('.kind'),function(k){a.kinds.push({type:q(k,'.kt'),weight:q(k,'.kw')})});
      m.actions.push(a)});
    return m})}
// the model of the editor -> hooks of the server's format (null with a message when something is missing)
function hooksToDef(models){
  var out=[];
  for(var i=0;i<models.length;i++){var m=models[i],h={},no='Hook '+(i+1)+': ';
    if(m.trig==='at'){var p=num(m.v);if(!(p>0&&p<=100))return no+'the percent must be above 0 and at most 100.';h.at=p}
    else if(m.trig==='at_time'){var mi=num(m.v);if(!(mi>0))return no+'the minutes must be above 0.';h.at_time=Math.max(1,Math.round(mi*60))}
    else if(m.trig==='kills'){var cr=String(m.creature).trim(),cn=num(m.count);if(!cr||!(cn>=1))return no+'name a creature and how many.';h.kills={creature:/^\d+$/.test(cr)?Number(cr):cr,count:Math.floor(cn)};if(m.per)h.kills.per_player=true;if(m.region)h.kills.region=Number(m.region)}
    else h.on=m.trig;
    h.actions=[];
    if(!m.actions.length)return no+'add at least one action.';
    for(var j=0;j<m.actions.length;j++){var a=m.actions[j],x={action:a.kind,target:a.target},at=no+'action '+(j+1)+': ';
      if(a.kind==='spawn'){var n=num(a.n);if(!(n>=1))return at+'how many creatures?';x.n=Math.min(40,Math.floor(n));var r=num(a.radius);if(r!=null)x.radius=Math.max(50,Math.min(3000,Math.round(r)));x.per_player=!!a.per;
        x.kinds=a.kinds.filter(function(k){return String(k.type).trim()}).map(function(k){var t=String(k.type).trim();return{type:/^\d+$/.test(t)?Number(t):t,weight:Math.max(1,Math.floor(num(k.weight)||1))}});
        if(!x.kinds.length)return at+'add at least one creature kind.'}
      else if(a.kind==='item'){var spec={},t=String(a.type).trim();if(t)spec.type=/^\d+$/.test(t)?Number(t):t;var q=num(a.quality);if(q!=null)spec.quality=q;if(String(a.category).trim())spec.category=String(a.category).trim().toLowerCase();var l=num(a.level);if(l!=null)spec.level=l;
        if(!Object.keys(spec).length)return at+'name an item or give a quality / kind / level.';x.spec=spec;x.n=Math.max(1,Math.min(10,Math.floor(num(a.n)||1)))}
      else if(a.kind==='gold'){var g=num(a.amount);if(!(g>=1))return at+'how much gold?';x.amount=Math.floor(g)}
      else if(!String(a.text).trim())return at+'write the message.';
      if(String(a.text).trim())x.text=String(a.text).trim();
      h.actions.push(x)}
    out.push(h)}
  return out}
function hooksHtml(models){
  return '<fieldset id="hooks"><legend>Hooks: what happens on the way</legend>'
   +'<p class="hint dim small">A hook fires once when its trigger happens (all that were passed fire if the progress jumps), then runs its actions in order. Waves are spawned by the target hero\'s own game; items and gold by the server. Limits: 40 creatures per action, 120 per minute for all hooks.</p>'
   +'<div id="hklist">'+models.map(hookHtml).join('')+'</div><button type="button" id="hkadd">Add a hook</button></fieldset>'}
function wireHooks(dlg){
  var list=dlg.querySelector('#hklist');
  function redo(models){list.innerHTML=models.map(hookHtml).join('')}
  function blank(kind){return{kind:kind,target:DEF_TARGET[kind],text:'',n:kind==='spawn'?5:1,radius:400,per:true,kinds:kind==='spawn'?[{type:'',weight:1}]:[],amount:100,type:'',quality:'',category:'',level:''}}
  function move(arr,i,d){var j=i+d;if(j<0||j>=arr.length)return;var t=arr[i];arr[i]=arr[j];arr[j]=t}
  dlg.querySelector('#hkadd').onclick=function(){var ms=readHooks(list);ms.push({trig:'at',v:50,creature:'',count:1,per:false,actions:[blank('spawn')]});redo(ms)};
  list.addEventListener('change',function(ev){var t=ev.target;if(t.classList.contains('tr')||t.classList.contains('ak'))redo(readHooks(list))});
  list.addEventListener('click',function(ev){
    var b=ev.target.closest('button[data-hk]');if(!b)return;
    var ms=readHooks(list),hk=b.closest('.hk'),hi=[].indexOf.call(list.children,hk),what=b.dataset.hk;
    if(what==='rm')ms.splice(hi,1);else if(what==='up')move(ms,hi,-1);else if(what==='down')move(ms,hi,1);
    else{
      var ae=b.closest('.act'),ai=ae?[].indexOf.call(hk.querySelectorAll('.act'),ae):-1,acts=ms[hi].actions;
      if(what==='aadd')acts.push(blank('spawn'));else if(what==='arm')acts.splice(ai,1);else if(what==='aup')move(acts,ai,-1);else if(what==='adown')move(acts,ai,1);
      else if(what==='kadd')acts[ai].kinds.push({type:'',weight:1});
      else if(what==='krm'){var ki=[].indexOf.call(ae.querySelectorAll('.kind'),b.closest('.kind'));acts[ai].kinds.splice(ki,1)}}
    redo(ms)})}
$('#reload').addEventListener('click',function(){status('');load()});
load();setInterval(function(){if(!$('#editor').open&&!document.hidden)load()},10000);
})();
