using System.Text;

namespace Sacred.GameServer.Web;

/// <summary>
/// <c>GET /sectors</c>: the live sector ownership map. The same canvas Ancaria map and sector grid as the area editor (<c>/api/areas/map</c>,
/// needs --game-dir), with every sector a player owns filled in that player's colour, occupied sectors outlined, the players as dots and
/// the owner and the players inside a sector on hover or tap. The data arrives as Server-Sent Events from <c>/api/sectors/stream</c>
/// (polling <c>/api/sectors</c> when the browser has no EventSource). One inline script, no libraries, works offline and on a phone.
/// </summary>
internal static partial class Pages
{
    private const string SectorsCss = """
.sm{display:grid;grid-template-columns:19rem minmax(0,1fr);gap:1rem;align-items:start}
.sm .side{display:flex;flex-direction:column;gap:.8rem;max-height:calc(100dvh - 9rem);overflow:auto}
.sm .stage{position:relative;background:#0c0a07;border:1px solid var(--line);border-radius:var(--r);overflow:hidden;box-shadow:var(--shadow);touch-action:none;height:calc(100dvh - 9rem);min-height:22rem}
.sm canvas{display:block;width:100%;height:100%;cursor:grab;touch-action:none}
.sm ul.pl{list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:.3rem}
.sm ul.pl li{display:flex;align-items:center;gap:.55rem;padding:.45rem .6rem;border:1px solid var(--line);border-radius:8px;cursor:pointer;min-height:2.5rem}
.sm ul.pl li:hover,.sm ul.pl li[aria-current=true]{border-color:var(--gold);background:var(--card2)}
.sm ul.pl .sw{width:.85rem;height:.85rem;border-radius:3px;flex:none;box-shadow:0 0 0 2px rgba(255,255,255,.08)}
.sm ul.pl .nm{min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.sm ul.pl small{color:var(--dim);margin-left:auto;white-space:nowrap}
.sm ul.pl li.off{opacity:.55}
.sm .tip{position:absolute;z-index:3;pointer-events:none;background:rgba(12,10,7,.92);color:#efe6d6;border:1px solid #4a3d2c;border-radius:8px;padding:.45rem .65rem;font-size:.85rem;line-height:1.45;max-width:16rem;box-shadow:0 6px 20px rgba(0,0,0,.5)}
.sm .tip b{color:#f2c36b}.sm .tip .dot{display:inline-block;width:.6rem;height:.6rem;border-radius:2px;margin-right:.3rem;vertical-align:baseline}
.sm .tip .dim{color:#a39683}
.sm .smlive{position:absolute;right:.6rem;top:.6rem;display:flex;align-items:center;gap:.4rem;background:rgba(12,10,7,.78);color:#efe6d6;border-radius:999px;padding:.2rem .65rem;font-size:.8rem;pointer-events:none}
.sm .smlive i{width:.55rem;height:.55rem;border-radius:50%;background:var(--bad)}
.sm .smlive.on i{background:var(--ok);animation:smp 2s ease-in-out infinite}
@keyframes smp{50%{opacity:.35}}
.sm .readout{position:absolute;left:.5rem;bottom:.5rem;background:rgba(12,10,7,.78);color:#efe6d6;border-radius:6px;padding:.25rem .5rem;font-size:.82rem;pointer-events:none;max-width:calc(100% - 1rem)}
.sm .hint{color:var(--dim);font-size:.85rem}
.sm .stats{display:flex;gap:1rem;flex-wrap:wrap}.sm .stats b{display:block;font-size:1.3rem;color:var(--gold2)}.sm .stats span{color:var(--dim);font-size:.8rem}
.sm .row{display:flex;gap:.4rem;flex-wrap:wrap}.sm .row button{flex:1 1 auto;min-height:2.6rem}
@media (max-width:860px){.sm{grid-template-columns:minmax(0,1fr)}.sm>*{min-width:0}.sm .stage{height:62dvh;order:-1}.sm .side{max-height:none}}
@media (prefers-reduced-motion:reduce){.sm .smlive.on i{animation:none}}
""";

    private const string SectorsScript = """
(function(){'use strict';
var TILE=53.66563034057617, ST=64;
var COLORS=['#e0a944','#6fa3d6','#79b873','#d4604d','#b48ad8','#4ec9c0','#e08fb8','#c4c46a'];
var $=function(s){return document.querySelector(s)};
var M=null, img=new Image(), imgOk=false, cellId={}, idCell={}, byId={};
var data={version:0,sectors:[],players:[]}, bySector={}, byPlayer={};
var view={x:0,y:0,k:1}, fitted=false, hover=null, pinned=null, sel=0, dpr=1;
var cv=$('#cv'), g=cv.getContext('2d');

function api(u){return fetch(u).then(function(r){if(!r.ok)throw new Error('HTTP '+r.status);return r.json()})}
function color(slot){return COLORS[(slot-1+COLORS.length*4)%COLORS.length]}
function mapOfTile(tx,ty){return [M.projection.ox+(tx-ty)*M.projection.kx, M.projection.oy+(tx+ty)*M.projection.ky]}
function tileOfMap(mx,my){var u=(mx-M.projection.ox)/M.projection.kx, v=(my-M.projection.oy)/M.projection.ky;return [(u+v)/2,(v-u)/2]}
function screenOfMap(mx,my){return [view.x+mx*view.k, view.y+my*view.k]}
function mapOfScreen(sx,sy){return [(sx-view.x)/view.k,(sy-view.y)/view.k]}
function cellOfScreen(sx,sy){var m=mapOfScreen(sx,sy),t=tileOfMap(m[0],m[1]);return [Math.floor(t[0]/ST),Math.floor(t[1]/ST)]}
function diamond(cx,cy){return [mapOfTile(cx*ST,cy*ST),mapOfTile((cx+1)*ST,cy*ST),mapOfTile((cx+1)*ST,(cy+1)*ST),mapOfTile(cx*ST,(cy+1)*ST)]}
function visible(p,w,h){for(var i=0;i<p.length;i++){var s=screenOfMap(p[i][0],p[i][1]);if(s[0]>-60&&s[0]<w+60&&s[1]>-60&&s[1]<h+60)return true}return false}
function path(p){var s=screenOfMap(p[0][0],p[0][1]);g.moveTo(s[0],s[1]);for(var i=1;i<p.length;i++){s=screenOfMap(p[i][0],p[i][1]);g.lineTo(s[0],s[1])}g.closePath()}
function playerPoint(p){var t=null;if(p.hasPosition)t=[p.x/TILE,p.y/TILE];else if(p.sector&&idCell[p.sector])t=[(idCell[p.sector][0]+.5)*ST,(idCell[p.sector][1]+.5)*ST];
  if(!t)return null;var m=mapOfTile(t[0],t[1]);return screenOfMap(m[0],m[1])}

function resize(){dpr=window.devicePixelRatio||1;var r=cv.getBoundingClientRect();cv.width=Math.max(1,Math.round(r.width*dpr));cv.height=Math.max(1,Math.round(r.height*dpr));
  if(M&&!fitted&&r.width>10&&r.height>10){fit();fitted=true}render()}
function fit(){if(!M)return;var r=cv.getBoundingClientRect();if(r.width<10||r.height<10)return;var k=Math.min(r.width/M.width,r.height/M.height);view.k=k;view.x=(r.width-M.width*k)/2;view.y=(r.height-M.height*k)/2}
function zoomToSectors(ids){if(!M||!ids.length)return;var r=cv.getBoundingClientRect(),x0=1e9,y0=1e9,x1=-1e9,y1=-1e9;
  ids.forEach(function(id){var c=idCell[id];if(!c)return;diamond(c[0],c[1]).forEach(function(p){x0=Math.min(x0,p[0]);y0=Math.min(y0,p[1]);x1=Math.max(x1,p[0]);y1=Math.max(y1,p[1])})});
  if(x0>x1)return;var pad=Math.max(40,(x1-x0)*.2,(y1-y0)*.2);x0-=pad;x1+=pad;y0-=pad;y1+=pad;
  var k=Math.min(r.width/(x1-x0),r.height/(y1-y0),8);view.k=k;view.x=(r.width-(x1-x0)*k)/2-x0*k;view.y=(r.height-(y1-y0)*k)/2-y0*k;fitted=true;draw()}

/* ---- the sector grid: built once as long straight runs in map pixels and stroked under the view transform ---- */
var gridPath=null, raf=0;
function hasCell(x,y){return !!cellId[x+','+y]}
function buildGrid(){var H={},V={},p=new Path2D();function add(o,k,v){(o[k]||(o[k]=[])).push(v)}
  M.sectors.forEach(function(c){var x=c.x,y=c.y;add(H,y,x);if(!hasCell(x,y+1))add(H,y+1,x);add(V,x,y);if(!hasCell(x+1,y))add(V,x+1,y)});
  function runs(o,seg){Object.keys(o).forEach(function(k){var l=o[k].sort(function(a,b){return a-b}),i=0;while(i<l.length){var j=i;while(j+1<l.length&&l[j+1]<=l[j]+1)j++;seg(+k,l[i],l[j]+1);i=j+1}})}
  function line(a,b){p.moveTo(a[0],a[1]);p.lineTo(b[0],b[1])}
  runs(H,function(y,x0,x1){line(mapOfTile(x0*ST,y*ST),mapOfTile(x1*ST,y*ST))});runs(V,function(x,y0,y1){line(mapOfTile(x*ST,y0*ST),mapOfTile(x*ST,y1*ST))});gridPath=p}
// pointer moves, wheel steps and live updates all ask for a redraw: draw at most once per frame
function draw(){if(!M||raf)return;raf=requestAnimationFrame(function(){raf=0;render()})}
function render(){if(!M)return;if(!gridPath)buildGrid();var w=cv.width/dpr,h=cv.height/dpr;g.setTransform(dpr,0,0,dpr,0,0);g.fillStyle='#0c0a07';g.fillRect(0,0,w,h);
  if(imgOk){g.imageSmoothingEnabled=view.k<1.5;g.globalAlpha=.8;g.drawImage(img,view.x,view.y,M.width*view.k,M.height*view.k);g.globalAlpha=1}
  var cellpx=view.k*Math.hypot(M.projection.kx,M.projection.ky)*ST,grid=cellpx>8;
  if(grid){g.setTransform(dpr*view.k,0,0,dpr*view.k,dpr*view.x,dpr*view.y);g.lineWidth=1/view.k;g.strokeStyle='rgba(255,255,255,'+(cellpx<18?.07:.14)+')';g.stroke(gridPath);g.setTransform(dpr,0,0,dpr,0,0)}
  var focus=sel||0;
  data.sectors.forEach(function(s){var c=idCell[s.id];if(!c)return;var d=diamond(c[0],c[1]);if(!visible(d,w,h))return;
    var col=s.owner?color(s.owner):'#9a9a9a';
    g.beginPath();path(d);g.globalAlpha=s.owner?(focus&&focus!==s.owner?.25:.55):.15;g.fillStyle=col;g.fill();g.globalAlpha=1;
    g.lineWidth=s.owner?2:1.2;g.strokeStyle=col;if(!s.owner||s.players.length&&s.players.indexOf(s.owner)<0)g.setLineDash([5,4]);g.stroke();g.setLineDash([])});
  if(hover&&hover.id){var c2=idCell[hover.id];if(c2){g.beginPath();path(diamond(c2[0],c2[1]));g.strokeStyle='#fff';g.lineWidth=2;g.stroke()}}
  data.players.forEach(function(p){var s=playerPoint(p);if(!s)return;var col=color(p.slot);
    g.beginPath();g.arc(s[0],s[1],p.slot===sel?9:7,0,7);g.fillStyle=col;g.fill();g.lineWidth=2.5;g.strokeStyle='#fff';g.stroke();
    g.font='600 12px system-ui,sans-serif';g.lineWidth=3.5;g.strokeStyle='rgba(0,0,0,.85)';g.strokeText(p.name,s[0]+11,s[1]+4);g.fillStyle='#fff';g.fillText(p.name,s[0]+11,s[1]+4)})}

function index(){bySector={};byPlayer={};data.sectors.forEach(function(s){bySector[s.id]=s});data.players.forEach(function(p){byPlayer[p.slot]=p})}
function nameOf(slot){var p=byPlayer[slot];return p?p.name:'player '+slot}
function renderList(){var ul=$('#players');ul.textContent='';
  data.players.forEach(function(p){var li=document.createElement('li'),sw=document.createElement('span'),nm=document.createElement('span'),sm=document.createElement('small');
    li.className=p.inGame?'':'off';li.setAttribute('aria-current',p.slot===sel?'true':'false');li.tabIndex=0;
    sw.className='sw';sw.style.background=color(p.slot);nm.className='nm';nm.textContent=p.name;
    sm.textContent=p.owned+' owned'+(p.sector?' - in '+p.sector:'');li.appendChild(sw);li.appendChild(nm);li.appendChild(sm);
    var go=function(){sel=sel===p.slot?0:p.slot;if(sel){var ids=data.sectors.filter(function(s){return s.owner===p.slot}).map(function(s){return s.id});if(!ids.length&&p.sector)ids=[p.sector];zoomToSectors(ids)}renderList();draw()};
    li.addEventListener('click',go);li.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();go()}});ul.appendChild(li)});
  if(!data.players.length){var e=document.createElement('li');e.className='hint';e.style.cursor='default';e.textContent='Nobody is online.';ul.appendChild(e)}
  var owned=data.sectors.filter(function(s){return s.owner}).length;$('#nOwned').textContent=owned;$('#nActive').textContent=data.sectors.length;$('#nPlayers').textContent=data.players.length}

function tipHtml(id){var s=bySector[id],info=byId[id],t=document.createElement('div');
  function line(html){var d=document.createElement('div');d.appendChild(html);t.appendChild(d)}
  function txt(x){return document.createTextNode(x)}
  var b=document.createElement('b');b.appendChild(txt('Sector '+id+(info&&info.name?' "'+info.name+'"':'')));line(b);
  if(!s){var n=document.createElement('span');n.className='dim';n.appendChild(txt('Nobody owns or holds it.'));line(n);return t}
  var o=document.createElement('span');
  if(s.owner){var dot=document.createElement('span');dot.className='dot';dot.style.background=color(s.owner);o.appendChild(txt('Owner: '));o.appendChild(dot);o.appendChild(txt(nameOf(s.owner)+' (slot '+s.owner+')'))}
  else o.appendChild(txt('No owner'));line(o);
  if(s.players.length){var p=document.createElement('span');p.appendChild(txt('Held by: '+s.players.map(nameOf).join(', ')));line(p)}
  var i=document.createElement('span');i.className='dim';i.appendChild(txt(s.initialized?'scripts started':'scripts not started yet'));line(i);return t}
function showTip(id,x,y){var tip=$('#tip');tip.textContent='';if(!id){tip.hidden=true;return}tip.appendChild(tipHtml(id));tip.hidden=false;
  var r=cv.getBoundingClientRect(),tw=tip.offsetWidth,th=tip.offsetHeight,px=x+14,py=y+14;if(px+tw>r.width-4)px=x-tw-14;if(py+th>r.height-4)py=y-th-14;tip.style.left=Math.max(4,px)+'px';tip.style.top=Math.max(4,py)+'px'}

var ptrs={}, drag=null, pinch=null, tapStart=null;
function pos(e){var r=cv.getBoundingClientRect();return [e.clientX-r.left,e.clientY-r.top]}
function dist(){var k=Object.keys(ptrs),a=ptrs[k[0]],b=ptrs[k[1]];return Math.hypot(a[0]-b[0],a[1]-b[1])||1}
function center(){var k=Object.keys(ptrs),a=ptrs[k[0]],b=ptrs[k[1]];return [(a[0]+b[0])/2,(a[1]+b[1])/2]}
function probe(p,pin){var c=cellOfScreen(p[0],p[1]),id=cellId[c[0]+','+c[1]]||0;hover=id?{id:id}:null;
  $('#readout').textContent=id?'Sector '+id:'';showTip(id,p[0],p[1]);if(pin)pinned=id;draw()}
cv.addEventListener('pointerdown',function(e){cv.setPointerCapture(e.pointerId);var p=pos(e);ptrs[e.pointerId]=p;
  if(Object.keys(ptrs).length>=2){pinch={d:dist(),c:center(),v:{x:view.x,y:view.y,k:view.k}};drag=null;return}
  drag={x:p[0],y:p[1],vx:view.x,vy:view.y,moved:false};tapStart=e.pointerType==='mouse'?null:p});
cv.addEventListener('pointermove',function(e){var p=pos(e);if(ptrs[e.pointerId])ptrs[e.pointerId]=p;
  if(pinch&&Object.keys(ptrs).length>=2){var k=Math.max(.05,Math.min(40,pinch.v.k*dist()/pinch.d)),c=center(),m=[(pinch.c[0]-pinch.v.x)/pinch.v.k,(pinch.c[1]-pinch.v.y)/pinch.v.k];view.k=k;view.x=c[0]-m[0]*k;view.y=c[1]-m[1]*k;showTip(0);draw();return}
  if(drag&&ptrs[e.pointerId]){if(Math.hypot(p[0]-drag.x,p[1]-drag.y)>5)drag.moved=true;if(drag.moved){view.x=drag.vx+p[0]-drag.x;view.y=drag.vy+p[1]-drag.y;showTip(0);draw();return}}
  if(e.pointerType==='mouse')probe(p,false)});
function up(e){var p=pos(e);delete ptrs[e.pointerId];if(Object.keys(ptrs).length<2)pinch=null;
  if(drag&&!drag.moved&&e.type==='pointerup')probe(p,true);drag=null}
cv.addEventListener('pointerup',up);cv.addEventListener('pointercancel',up);
cv.addEventListener('pointerleave',function(e){if(e.pointerType==='mouse'){hover=null;$('#readout').textContent='';showTip(0);draw()}});
cv.addEventListener('wheel',function(e){e.preventDefault();var p=pos(e),m=mapOfScreen(p[0],p[1]),k=Math.max(.05,Math.min(40,view.k*Math.exp(-e.deltaY*.0015)));view.k=k;view.x=p[0]-m[0]*k;view.y=p[1]-m[1]*k;draw()},{passive:false});
window.addEventListener('resize',resize);if(window.ResizeObserver)new ResizeObserver(resize).observe(cv);
$('#fit').addEventListener('click',function(){fit();fitted=true;draw()});
$('#owned').addEventListener('click',function(){zoomToSectors(data.sectors.map(function(s){return s.id}))});

/* ---- live data: SSE, with polling as the fallback ---- */
function apply(d){data=d;index();renderList();if(hover&&hover.id)showTip(hover.id,0,0);draw();var t=$('#stamp');t.textContent=d.timeUnixMs?new Date(d.timeUnixMs).toLocaleTimeString():''}
function live(on){var e=$('#live');e.className="smlive"+(on?" on":"");$('#liveText').textContent=on?'live':'reconnecting'}
var poller=null;
function startPolling(){if(poller)return;poller=setInterval(function(){api('api/sectors').then(function(d){live(true);if(d.version!==data.version)apply(d)}).catch(function(){live(false)})},1500)}
function connect(){if(!window.EventSource){startPolling();return}
  var es=new EventSource('api/sectors/stream');
  es.addEventListener('sectors',function(e){try{apply(JSON.parse(e.data));live(true)}catch(x){}});
  es.onopen=function(){live(true)};es.onerror=function(){live(false)}}

/* ---- start ---- */
api('api/areas/map').then(function(m){M=m;m.sectors.forEach(function(s){cellId[s.x+','+s.y]=s.id;idCell[s.id]=[s.x,s.y];byId[s.id]=s});
  img.onload=function(){imgOk=true;draw()};img.src='api/areas/map.png';$('#loading').hidden=true;resize();
  return api('api/sectors')}).then(function(d){apply(d);connect()}).catch(function(e){var l=$('#loading');l.hidden=false;l.textContent='The map is not available: '+e.message+' (the server needs --game-dir)';l.className='hint'});
})();
""";

    /// <summary><c>GET /sectors</c>: live sector ownership map for admins (see <see cref="SectorsCss"/> and <see cref="SectorsScript"/>).</summary>
    public static string SectorsPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(10 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1, viewport-fit=cover\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\"><script>try{var m=localStorage.getItem('gsTheme');if(m==='light'||m==='dark')document.documentElement.dataset.theme=m;}catch(e){}</script><title>Sectors - ")
          .Append(E(o.Name)).Append(" - Sacred GameServer</title><style>").Append(Style).Append(SectorsCss).Append("</style></head><body><div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "sectors", admin.Mods().Count);
        sb.Append("</header><main class=\"sm\"><div class=\"side\">")
          .Append("<section class=\"card\"><h2>Sector ownership</h2><div class=\"stats\"><div><b id=\"nOwned\">0</b><span>owned sectors</span></div><div><b id=\"nActive\">0</b><span>owned or held</span></div><div><b id=\"nPlayers\">0</b><span>players</span></div></div>")
          .Append("<p class=\"hint\">The owner runs a sector's scripts and creatures. Filled: owned, in the owner's colour. Dashed outline: nobody owns it, or its owner is not inside it. Hover or tap a sector for details.</p>")
          .Append("<div class=\"row\"><button type=\"button\" id=\"fit\">Fit map</button><button type=\"button\" id=\"owned\">Show active</button></div></section>")
          .Append("<section class=\"card\"><h2>Players <small id=\"stamp\" style=\"font-weight:400;color:var(--dim)\"></small></h2><ul class=\"pl\" id=\"players\"></ul></section>")
          .Append("</div><div class=\"stage\"><canvas id=\"cv\" aria-label=\"World map: which player owns which sector\"></canvas>")
          .Append("<p class=\"hint\" id=\"loading\" style=\"position:absolute;top:.6rem;left:.8rem;margin:0;color:#efe6d6\">Loading the map...</p>")
          .Append("<div class=\"smlive\" id=\"live\"><i></i><span id=\"liveText\">connecting</span></div><div class=\"tip\" id=\"tip\" hidden></div><div class=\"readout\" id=\"readout\"></div></div>")
          .Append("</main></div><script>").Append(SectorsScript).Append("</script></body></html>");
        return sb.ToString();
    }
}
