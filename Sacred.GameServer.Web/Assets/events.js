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
      +'<td>'+esc(e.goal||'')+'<br><small>'+(d.target||'?')+' kills in '+dur(d.duration||0)+(d.area||d.areas?' &middot; area '+esc([].concat(d.area||[],d.areas||[]).join(', ')):'')+'</small></td>'
      +'<td>'+(e.running?'<span class="pill ok">running</span>':e.enabled?'<span class="pill">in rotation</span>':'<span class="pill warn">off</span>')+'</td>'
      +'<td class="act"><button type="button" data-a="start" data-i="'+i+'">Start now</button><button type="button" data-a="edit" data-i="'+i+'">Edit</button>'
      +'<button type="button" data-a="toggle" data-i="'+i+'">'+(e.enabled?'Turn off':'Turn on')+'</button>'
      +(e.source==='web'?'<button type="button" class="bad" data-a="del" data-i="'+i+'">Delete</button>':e.source==='override'?'<button type="button" data-a="reset" data-i="'+i+'" title="Back to the script file\'s version">Reset</button>':'')+'</td></tr>';
  });
  $('#events').innerHTML=rows||'<tr><td colspan="4" class="dim">No events yet.</td></tr>';
}

function webMap(){var m={};data.events.forEach(function(e){if((e.source==='web'||e.source==='override')&&!e.invalid)m[e.id]=e.def});return m}
function offMap(){var m={};data.events.forEach(function(e){if(!e.enabled)m[e.id]=true});return m}
function persist(what){status('Saving...');return post({events:webMap(),events_off:offMap()}).then(function(){status('Saved. Waiting for the server to pick it up...');
  return new Promise(function(ok){setTimeout(ok,2600)})}).then(load).then(function(){status(what+' Done: the server is using it.','ok')}).catch(function(e){status(e.message,'bad')})}
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
   +'<fieldset><legend>Where (none picked: everywhere)</legend>'+(ar||'<small>No areas yet: draw some on the Areas page.</small>')
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
   +'<p class="status bad" id="eerr"></p><div class="row" style="justify-content:flex-end"><button type="button" id="ecancel" class="ghost">Cancel</button><button class="primary" id="esave">Save</button></div></form>';
  var dlg=$('#editor');dlg.innerHTML=h;dlg.querySelector('[name=where]').value=d.where||'';
  dlg.querySelector('#ecancel').onclick=function(){dlg.close()};
  dlg.querySelector('#addms').onclick=function(){var t=document.createElement('tbody');t.innerHTML=msRow({at:'',weights:{}},tiers);dlg.querySelector('#ms').appendChild(t.firstChild)};
  dlg.querySelector('#ms').addEventListener('click',function(ev){var b=ev.target.closest('button.rm');if(b)b.closest('tr').remove()});
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
  def.reward=rw;def.created=(edit&&edit.def.created)||Date.now();
  var entry={id:id,source:edit?(edit.source==='file'?'override':edit.source):'web',enabled:edit?edit.enabled:true,def:def};
  if(edit){var at=data.events.indexOf(edit);data.events[at]=entry}else data.events.push(entry);
  dlg.close();persist('Saved "'+def.title+'".');
}
$('#reload').addEventListener('click',function(){status('');load()});
load();setInterval(function(){if(!$('#editor').open&&!document.hidden)load()},10000);
})();
