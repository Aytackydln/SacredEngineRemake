(function(){'use strict';
var TILE=53.66563034057617, ST=64, COLORS=['#e0a944','#6fa3d6','#79b873','#d4604d','#b48ad8','#4ec9c0','#e08fb8','#c4c46a'];
var $=function(s){return document.querySelector(s)};
var M=null, img=new Image(), imgOk=false;
var areas={}, order=[], cur=null, tool='pan', events=[];
var cellId={}, idCell={}, byId={};       // 'cx,cy' -> id; id -> [cx,cy]; id -> sector info
var players=[], hover=null, view={x:0,y:0,k:1}, drawing=null, dirtySaved=false;
var cv=$('#cv'), g=cv.getContext('2d'), dpr=1;

function status(t,cls){var e=$('#status');e.textContent=t||'';e.className='status '+(cls||'')}
function api(url,opts){return fetch(url,opts).then(function(r){return r.text().then(function(t){var j=null;try{j=JSON.parse(t)}catch(e){}
  if(!r.ok)throw new Error((j&&j.error)||t||('HTTP '+r.status));return j})})}

/* ---- the sector geometry: tiles <-> map pixels (the game's own world map projection) ---- */
function mapOfTile(tx,ty){return [M.projection.ox+(tx-ty)*M.projection.kx, M.projection.oy+(tx+ty)*M.projection.ky]}
function tileOfMap(mx,my){var u=(mx-M.projection.ox)/M.projection.kx, v=(my-M.projection.oy)/M.projection.ky;return [(u+v)/2,(v-u)/2]}
function screenOfMap(mx,my){return [view.x+mx*view.k, view.y+my*view.k]}
function mapOfScreen(sx,sy){return [(sx-view.x)/view.k,(sy-view.y)/view.k]}
function cellOfScreen(sx,sy){var m=mapOfScreen(sx,sy),t=tileOfMap(m[0],m[1]);return [Math.floor(t[0]/ST),Math.floor(t[1]/ST),t]}
function diamond(cx,cy){var a=mapOfTile(cx*ST,cy*ST),b=mapOfTile((cx+1)*ST,cy*ST),c=mapOfTile((cx+1)*ST,(cy+1)*ST),d=mapOfTile(cx*ST,(cy+1)*ST);return [a,b,c,d]}

/* ---- areas: the same format as world_events.lua's AREAS ---- */
function expand(list,into){(list||[]).forEach(function(p){if(Array.isArray(p)){var a=Math.min(p[0],p[1]),b=Math.max(p[0],p[1]);for(var i=a;i<=b&&i-a<5000;i++)into[i]=true}else if(typeof p==='number')into[p]=true})}
function fromDef(id,d,source){var ids={};expand(d.sectors,ids);expand(d.ranges,ids);
  if(d.rect&&d.rect.length>=4){for(var x=Math.min(d.rect[0],d.rect[2]);x<=Math.max(d.rect[0],d.rect[2]);x++)for(var y=Math.min(d.rect[1],d.rect[3]);y<=Math.max(d.rect[1],d.rect[3]);y++){var s=cellId[x+','+y];if(s)ids[s]=true}}
  return {id:id,name:d.name||id,ids:ids,near:d.near||0,circle:d.circle?{x:d.circle.x,y:d.circle.y,r:d.circle.r}:null,source:source,changed:false}}
function compact(sorted){var out=[],i=0;while(i<sorted.length){var j=i;while(sorted[j+1]===sorted[j]+1)j++;if(j-i>=2)out.push([sorted[i],sorted[j]]);else for(var k=i;k<=j;k++)out.push(sorted[k]);i=j+1}return out}
function sortedIds(a){return Object.keys(a.ids).map(Number).sort(function(x,y){return x-y})}
function toDef(a){var d={name:a.name,sectors:compact(sortedIds(a))};if(a.near>0)d.near=a.near;if(a.circle)d.circle={x:Math.round(a.circle.x),y:Math.round(a.circle.y),r:Math.round(a.circle.r)};return d}
function luaString(t){return '"'+String(t).replace(/[\u0000-\u001f]/g,' ').replace(/\\/g,'\\\\').replace(/"/g,'\\"')+'"'}
function compactText(list){return list.map(function(v){return Array.isArray(v)?'{'+v[0]+', '+v[1]+'}':String(v)}).join(', ')}
function snippet(a){var p=['name = '+luaString(a.name)],s=compact(sortedIds(a));if(s.length)p.push('sectors = {'+compactText(s)+'}');if(a.near>0)p.push('near = '+a.near);
  if(a.circle)p.push('circle = { x = '+Math.round(a.circle.x)+', y = '+Math.round(a.circle.y)+', r = '+Math.round(a.circle.r)+' }');return a.id+' = { '+p.join(', ')+' },'}
function slug(n){var s=String(n||'').toLowerCase().replace(/[^a-z0-9]+/g,'_').replace(/^_+|_+$/g,'');if(!s)s='area';if(/^\d/.test(s))s='area_'+s;return s}
window.AreaCodec={compact:compact,toDef:toDef,fromDefIds:function(d){var ids={};expand(d.sectors,ids);expand(d.ranges,ids);return ids},snippet:snippet,slug:slug,luaString:luaString,
  geometry:{TILE:TILE,ST:ST}};

function color(a){var i=order.indexOf(a.id);return COLORS[(i<0?0:i)%COLORS.length]}
function count(a){return Object.keys(a.ids).length}

/* ---- drawing ---- */
var fitted=false;
function resize(){dpr=window.devicePixelRatio||1;var r=cv.getBoundingClientRect();cv.width=Math.max(1,Math.round(r.width*dpr));cv.height=Math.max(1,Math.round(r.height*dpr));if(M&&!fitted&&r.width>10&&r.height>10){fit();fitted=true}draw()}
function fit(){if(!M)return;var r=cv.getBoundingClientRect();if(r.width<10||r.height<10)return;var k=Math.min(r.width/M.width,r.height/M.height);view.k=k;view.x=(r.width-M.width*k)/2;view.y=(r.height-M.height*k)/2}
function zoomTo(a){if(!M||!a)return;var r=cv.getBoundingClientRect();if(r.width<10)return;var x0=1e9,y0=1e9,x1=-1e9,y1=-1e9,any=false;
  function add(mx,my){any=true;x0=Math.min(x0,mx);y0=Math.min(y0,my);x1=Math.max(x1,mx);y1=Math.max(y1,my)}
  M.sectors.forEach(function(s){if(a.ids[s.id])diamond(s.x,s.y).forEach(function(p){add(p[0],p[1])})});
  if(a.circle)for(var t=0;t<16;t++){var an=t/16*Math.PI*2,p=mapOfTile((a.circle.x+Math.cos(an)*a.circle.r)/TILE,(a.circle.y+Math.sin(an)*a.circle.r)/TILE);add(p[0],p[1])}
  if(!any)return;var pad=Math.max(40,(x1-x0)*.8,(y1-y0)*.8);x0-=pad;x1+=pad;y0-=pad;y1+=pad;
  var k=Math.min(r.width/(x1-x0),r.height/(y1-y0));k=Math.min(k,8);view.k=k;view.x=(r.width-(x1-x0)*k)/2-x0*k;view.y=(r.height-(y1-y0)*k)/2-y0*k;fitted=true;draw()}
function visible(p,w,h){for(var i=0;i<p.length;i++){var s=screenOfMap(p[i][0],p[i][1]);if(s[0]>-60&&s[0]<w+60&&s[1]>-60&&s[1]<h+60)return true}return false}
function path(p){var s=screenOfMap(p[0][0],p[0][1]);g.moveTo(s[0],s[1]);for(var i=1;i<p.length;i++){s=screenOfMap(p[i][0],p[i][1]);g.lineTo(s[0],s[1])}g.closePath()}
function draw(){if(!M)return;var w=cv.width/dpr,h=cv.height/dpr;g.setTransform(dpr,0,0,dpr,0,0);g.clearRect(0,0,w,h);g.fillStyle='#0c0a07';g.fillRect(0,0,w,h);
  if(imgOk){g.imageSmoothingEnabled=view.k<1.5;g.drawImage(img,view.x,view.y,M.width*view.k,M.height*view.k)}
  var cellpx=view.k*Math.hypot(M.projection.kx,M.projection.ky)*ST,grid=cellpx>6,gridAlpha=cellpx<14?.1:cellpx<30?.2:.3;
  g.lineWidth=1;
  M.sectors.forEach(function(s){var d=diamond(s.x,s.y);if(!visible(d,w,h))return;
    var mine=null;for(var i=0;i<order.length;i++){var a=areas[order[i]];if(a.ids[s.id]){mine=a;if(a===areas[cur])break}}
    if(mine){g.beginPath();path(d);g.globalAlpha=mine===areas[cur]?.5:.28;g.fillStyle=color(mine);g.fill();g.globalAlpha=1}
    if(grid){g.beginPath();path(d);g.strokeStyle='rgba(255,255,255,'+gridAlpha+')';g.stroke()}});
  var a=areas[cur];if(a){g.beginPath();M.sectors.forEach(function(s){if(a.ids[s.id]){var d=diamond(s.x,s.y);if(visible(d,w,h))path(d)}});g.strokeStyle=color(a);g.lineWidth=2;g.stroke()}
  order.forEach(function(id){var b=areas[id];if(b.circle){var c=mapOfTile(b.circle.x/TILE,b.circle.y/TILE);var s=screenOfMap(c[0],c[1]);
    var rx=b.circle.r/TILE*view.k*(M.projection.kx*Math.SQRT2*0),ry=0;
    // the circle on the ground is an ellipse on the isometric map: draw its outline point by point
    g.beginPath();for(var t=0;t<=64;t++){var an=t/64*Math.PI*2;var p=mapOfTile((b.circle.x+Math.cos(an)*b.circle.r)/TILE,(b.circle.y+Math.sin(an)*b.circle.r)/TILE);var q=screenOfMap(p[0],p[1]);if(t)g.lineTo(q[0],q[1]);else g.moveTo(q[0],q[1])}
    g.closePath();g.globalAlpha=id===cur?.35:.18;g.fillStyle=color(b);g.fill();g.globalAlpha=1;g.strokeStyle=color(b);g.lineWidth=id===cur?2.5:1.5;g.stroke()}});
  if(drawing&&drawing.type==='rect'){var p0=drawing.a,p1=drawing.b;g.beginPath();[[p0[0],p0[1]],[p1[0]+1,p0[1]],[p1[0]+1,p1[1]+1],[p0[0],p1[1]+1]].forEach(function(c,i){var m=mapOfTile(c[0]*ST,c[1]*ST),s=screenOfMap(m[0],m[1]);if(i)g.lineTo(s[0],s[1]);else g.moveTo(s[0],s[1])});g.closePath();g.strokeStyle='#fff';g.setLineDash([6,4]);g.lineWidth=2;g.stroke();g.setLineDash([])}
  if(hover){var d=diamond(hover.cx,hover.cy);g.beginPath();path(d);g.strokeStyle='#fff';g.lineWidth=2;g.stroke()}
  players.forEach(function(p){var t=p.x||p.y?[p.x/TILE,p.y/TILE]:null;if(!t&&p.sector&&idCell[p.sector]){t=[(idCell[p.sector][0]+.5)*ST,(idCell[p.sector][1]+.5)*ST]}if(!t)return;
    var m=mapOfTile(t[0],t[1]),s=screenOfMap(m[0],m[1]);g.beginPath();g.arc(s[0],s[1],5,0,7);g.fillStyle='#fff';g.fill();g.strokeStyle='#000';g.lineWidth=2;g.stroke();
    g.font='12px system-ui,sans-serif';g.lineWidth=3;g.strokeStyle='rgba(0,0,0,.8)';g.strokeText(p.name,s[0]+8,s[1]+4);g.fillStyle='#fff';g.fillText(p.name,s[0]+8,s[1]+4)})}

/* ---- the lists ---- */
function renderAreas(){var ul=$('#areas');ul.innerHTML='';order.forEach(function(id){var a=areas[id],li=document.createElement('li');li.setAttribute('aria-current',id===cur?'true':'false');
  li.innerHTML='<span class="sw"></span><b></b><small></small>';li.querySelector('.sw').style.background=color(a);li.querySelector('b').textContent=a.name;
  li.querySelector('small').textContent=count(a)+' sectors'+(a.circle?' + circle':'')+(a.source==='file'&&!a.changed?' (file)':a.changed?' *':'');
  li.addEventListener('click',function(){cur=id;renderAreas();renderDetail();draw();if(count(a)+(a.circle?1:0)>0)zoomTo(a)});ul.appendChild(li)});
  if(!order.length)ul.innerHTML='<li class="hint">No areas yet: create one below.</li>'}
function renderDetail(){var a=areas[cur],box=$('#detail');box.hidden=!a;if(!a)return;$('#name').value=a.name;$('#near').value=a.near||0;
  $('#info').textContent=count(a)+' sectors'+(a.circle?'; circle at x '+Math.round(a.circle.x)+' y '+Math.round(a.circle.y)+', radius '+Math.round(a.circle.r):'')+'. id: '+a.id;
  $('#snip').value=snippet(a);$('#circleOff').hidden=!a.circle}
function touch(a){a.changed=true;dirtySaved=true;renderAreas();renderDetail();draw();$('#save').disabled=false}

/* ---- tools ---- */
function setTool(t){tool=t;document.querySelectorAll('[data-tool]').forEach(function(b){b.setAttribute('aria-pressed',b.dataset.tool===t?'true':'false')});cv.className=t==='pan'?'':'paint';
  $('#toolhint').textContent={pan:'Drag to move, wheel or pinch to zoom.',paint:'Drag over sectors to add them.',erase:'Drag over sectors to remove them.',rectadd:'Drag a rectangle of sector cells to add them.',rectdel:'Drag a rectangle to remove its sectors.',circle:'Click the centre, drag out the radius.'}[t]}
document.querySelectorAll('[data-tool]').forEach(function(b){b.addEventListener('click',function(){setTool(b.dataset.tool)})});
function paintAt(sx,sy,on){var a=areas[cur];if(!a)return;var c=cellOfScreen(sx,sy),id=cellId[c[0]+','+c[1]];if(!id)return;if(on&&!a.ids[id]){a.ids[id]=true;touch(a)}else if(!on&&a.ids[id]){delete a.ids[id];touch(a)}}
function applyRect(r,on){var a=areas[cur];if(!a)return;var x0=Math.min(r.a[0],r.b[0]),x1=Math.max(r.a[0],r.b[0]),y0=Math.min(r.a[1],r.b[1]),y1=Math.max(r.a[1],r.b[1]),n=0;
  for(var x=x0;x<=x1;x++)for(var y=y0;y<=y1;y++){var id=cellId[x+','+y];if(id){if(on&&!a.ids[id]){a.ids[id]=true;n++}else if(!on&&a.ids[id]){delete a.ids[id];n++}}}
  if(n)touch(a);status((on?'Added ':'Removed ')+n+' sectors.')}

var ptrs={}, pinch=null;
function pos(e){var r=cv.getBoundingClientRect();return [e.clientX-r.left,e.clientY-r.top]}
cv.addEventListener('pointerdown',function(e){cv.setPointerCapture(e.pointerId);ptrs[e.pointerId]=pos(e);var ids=Object.keys(ptrs);
  if(ids.length>=2){pinch={d:dist(),c:center(),view:{x:view.x,y:view.y,k:view.k}};drawing=null;return}
  var p=pos(e);if(tool==='pan'||e.button===1||e.button===2)drawing={type:'pan',x:p[0],y:p[1],vx:view.x,vy:view.y};
  else if(tool==='paint'||tool==='erase'){drawing={type:'paint'};paintAt(p[0],p[1],tool==='paint')}
  else if(tool==='rectadd'||tool==='rectdel'){var c=cellOfScreen(p[0],p[1]);drawing={type:'rect',a:[c[0],c[1]],b:[c[0],c[1]]}}
  else if(tool==='circle'){var c2=cellOfScreen(p[0],p[1]);drawing={type:'circle',t:c2[2]}}
  draw()});
function dist(){var k=Object.keys(ptrs),a=ptrs[k[0]],b=ptrs[k[1]];return Math.hypot(a[0]-b[0],a[1]-b[1])||1}
function center(){var k=Object.keys(ptrs),a=ptrs[k[0]],b=ptrs[k[1]];return [(a[0]+b[0])/2,(a[1]+b[1])/2]}
cv.addEventListener('pointermove',function(e){var p=pos(e);if(ptrs[e.pointerId])ptrs[e.pointerId]=p;
  if(pinch&&Object.keys(ptrs).length>=2){var k=Math.max(.05,Math.min(40,pinch.view.k*dist()/pinch.d));var c=center(),m=[(pinch.c[0]-pinch.view.x)/pinch.view.k,(pinch.c[1]-pinch.view.y)/pinch.view.k];view.k=k;view.x=c[0]-m[0]*k;view.y=c[1]-m[1]*k;draw();return}
  var c3=cellOfScreen(p[0],p[1]);var id=cellId[c3[0]+','+c3[1]];hover=id?{cx:c3[0],cy:c3[1],id:id}:{cx:c3[0],cy:c3[1],id:0};
  var s=id&&byId[id];$('#readout').textContent=(id?'Sector '+id+(s&&s.name?' "'+s.name+'"':''):'no sector')+'  cell '+c3[0]+','+c3[1]+'  x '+Math.round(c3[2][0]*TILE)+' y '+Math.round(c3[2][1]*TILE);
  if(drawing){if(drawing.type==='pan'){view.x=drawing.vx+p[0]-drawing.x;view.y=drawing.vy+p[1]-drawing.y}
    else if(drawing.type==='paint')paintAt(p[0],p[1],tool==='paint');
    else if(drawing.type==='rect')drawing.b=[c3[0],c3[1]];
    else if(drawing.type==='circle'){var a=areas[cur];if(a){var dx=c3[2][0]-drawing.t[0],dy=c3[2][1]-drawing.t[1];a.circle={x:drawing.t[0]*TILE,y:drawing.t[1]*TILE,r:Math.max(100,Math.hypot(dx,dy)*TILE)};drawing.moved=true;renderDetail()}}}
  draw()});
function up(e){delete ptrs[e.pointerId];if(Object.keys(ptrs).length<2)pinch=null;
  if(drawing&&drawing.type==='rect')applyRect(drawing,tool==='rectadd');
  if(drawing&&drawing.type==='circle'&&areas[cur]){var a=areas[cur];if(!drawing.moved)a.circle={x:drawing.t[0]*TILE,y:drawing.t[1]*TILE,r:a.circle?a.circle.r:1500};touch(a)}
  drawing=null;draw()}
cv.addEventListener('pointerup',up);cv.addEventListener('pointercancel',up);cv.addEventListener('contextmenu',function(e){e.preventDefault()});
cv.addEventListener('pointerleave',function(){hover=null;$('#readout').textContent='';draw()});
cv.addEventListener('wheel',function(e){e.preventDefault();var p=pos(e),m=mapOfScreen(p[0],p[1]),k=Math.max(.05,Math.min(40,view.k*Math.exp(-e.deltaY*.0015)));view.k=k;view.x=p[0]-m[0]*k;view.y=p[1]-m[1]*k;draw()},{passive:false});
window.addEventListener('resize',resize);
if(window.ResizeObserver)new ResizeObserver(function(){resize()}).observe(cv);

/* ---- buttons ---- */
$('#fit').addEventListener('click',function(){fit();fitted=true;draw()});
$('#zoomArea').addEventListener('click',function(){zoomTo(areas[cur])});
$('#new').addEventListener('click',function(){var n=$('#newName').value.trim();if(!n){status('Give the area a name first.','bad');return}var id=slug(n),base=id,i=2;while(areas[id])id=base+'_'+i++;
  areas[id]={id:id,name:n,ids:{},near:0,circle:null,source:'saved',changed:true};order.push(id);cur=id;$('#newName').value='';dirtySaved=true;$('#save').disabled=false;renderAreas();renderDetail();draw();setTool('paint');status('Area '+id+' created: paint its sectors.','ok')});
$('#name').addEventListener('change',function(){var a=areas[cur];if(a){a.name=$('#name').value.trim()||a.id;touch(a)}});
$('#near').addEventListener('change',function(){var a=areas[cur];if(a){a.near=Math.max(0,Math.min(3,Math.floor(+$('#near').value||0)));touch(a)}});
$('#circleOff').addEventListener('click',function(){var a=areas[cur];if(a){a.circle=null;touch(a)}});
$('#clear').addEventListener('click',function(){var a=areas[cur];if(a&&confirm('Remove all sectors and the circle of '+a.name+'?')){a.ids={};a.circle=null;touch(a)}});
$('#delete').addEventListener('click',function(){var a=areas[cur];if(!a)return;if(a.source==='file'){status('This area is in the script file: edit it there (or save a changed copy, which wins over the file).','bad');return}
  if(confirm('Delete the area '+a.name+'?')){delete areas[cur];order=order.filter(function(x){return x!==cur});cur=order[0]||null;dirtySaved=true;$('#save').disabled=false;renderAreas();renderDetail();draw()}});
$('#copy').addEventListener('click',function(){var t=$('#snip');t.focus();t.select();var done=false;try{done=document.execCommand('copy')}catch(e){}
  if(!done&&navigator.clipboard){navigator.clipboard.writeText(t.value).then(function(){status('Copied.','ok')},function(){status('Select the text and copy it.','bad')});return}status(done?'Copied.':'Select the text and copy it.',done?'ok':'bad')});
$('#save').addEventListener('click',function(){var out={};order.forEach(function(id){var a=areas[id];if(a.source==='saved'||a.changed)out[id]=toDef(a)});
  status('Saving...');api('api/mod-store',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mod:'world_events',set:{areas:out}})}).then(function(r){
    order.forEach(function(id){if(areas[id].changed){areas[id].source='saved';areas[id].changed=false}});dirtySaved=false;$('#save').disabled=true;renderAreas();
    status('Saved ('+Object.keys(out).length+' areas). The server picks them up within seconds.','ok')}).catch(function(e){status('Not saved: '+e.message,'bad')})});
window.addEventListener('beforeunload',function(e){if(dirtySaved){e.preventDefault();e.returnValue=''}});

/* ---- start ---- */
function indexMap(){M.sectors.forEach(function(s){cellId[s.x+','+s.y]=s.id;idCell[s.id]=[s.x,s.y];byId[s.id]=s})}
function loadAreas(){return api('mods/world_events/areas').then(function(r){Object.keys(r.areas||{}).sort().forEach(function(id){areas[id]=fromDef(id,r.areas[id],r.areas[id].source==='saved'?'saved':'file');order.push(id)});
  events=r.events||[];cur=order[0]||null;renderAreas();renderDetail();draw();if(cur&&count(areas[cur])+(areas[cur].circle?1:0)>0)zoomTo(areas[cur])}).catch(function(e){status('The world_events mod did not answer ('+e.message+'): areas cannot be loaded or saved.','bad');$('#save').disabled=true})}
function pollPlayers(){api('mods/world_events/whereabouts').then(function(r){players=r.players||[];draw()}).catch(function(){})}
setTool('pan');
api('api/areas/map').then(function(m){M=m;indexMap();img.onload=function(){imgOk=true;draw()};img.src='api/areas/map.png';resize();
  $('#loading').hidden=true;return loadAreas()}).then(function(){pollPlayers();setInterval(pollPlayers,4000)}).catch(function(e){$('#loading').textContent='The map is not available: '+e.message;$('#loading').className='status bad'});
})();
