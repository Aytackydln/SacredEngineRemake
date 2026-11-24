using System.Text;

namespace Sacred.GameServer.Web;

internal static partial class Pages
{
    private const string AuctionCss = """
.au{display:grid;gap:1rem;max-width:46rem}.au label{display:grid;grid-template-columns:13rem 1fr;gap:.5rem;align-items:center;margin:.35rem 0}
.au label small{display:block;color:var(--dim)}.au input[type=text]{width:100%}.au input[type=checkbox]{justify-self:start}
.au .status{min-height:1.2rem}.au .status.bad{color:var(--bad)}.au .status.ok{color:var(--ok)}
""";

    private const string AuctionScript = """
(function(){'use strict';
var MOD='auction_house',$=function(s){return document.querySelector(s)};
var FIELDS=[['durations','list','Durations','days a listing may run, comma separated'],['fee_per_day','num','Fee per day','percent of the price per listed day'],
['fee_min','num','Minimum fee','gold'],['sale_cut','num','Sale cut','percent of the price taken on a sale'],['max_per_seller','num','Max per seller','listings per seller'],
['max_listings','num','Total cap','listings in the whole market'],['allow_special','bool','Allow special items','special items may be listed'],
['price_min','num','Price min','gold'],['price_max','num','Price max','gold'],['sweep_seconds','num','Expiry interval','seconds between checks for expired listings (min 5)'],
['verify_seconds','num','Verify seconds','Closed Net: wait for the server to see an item or gold (min 5)'],['token_seconds','num','Token seconds','Closed Net: life of a delivery token (1 to 600)'],
['gold_timeout','num','Gold timeout','Closed Net: seconds until a gold withdrawal is settled from the saved hero (min 10)'],
['recent_sales','num','Recent sales','sales shown on the page'],['chat_notices','bool','Chat notices','sale notices as chat lines']];
function status(t,c){var e=$('#status');e.textContent=t||'';e.className='status '+(c||'')}
function api(url,o){return fetch(url,o).then(function(r){return r.text().then(function(t){var j=null;try{j=JSON.parse(t)}catch(e){}if(!r.ok)throw new Error((j&&j.error)||t||('HTTP '+r.status));return j})})}
function build(rules){var f=$('#form');f.textContent='';FIELDS.forEach(function(d){var l=document.createElement('label'),s=document.createElement('span'),i=document.createElement('input'),sm=document.createElement('small');
s.textContent=d[2];sm.textContent=d[3];s.appendChild(sm);i.id='f_'+d[0];var v=rules[d[0]];
if(d[1]==='bool'){i.type='checkbox';i.checked=!!v}else{i.type='text';i.value=Array.isArray(v)?v.join(', '):(v==null?'':v)}l.appendChild(s);l.appendChild(i);f.appendChild(l)})}
function load(){return api('mods/'+MOD+'/rules').then(function(r){$('#missing').hidden=true;$('#main').hidden=false;build(r)}).catch(function(e){$('#main').hidden=true;$('#missing').hidden=false;$('#why').textContent=e.message})}
$('#save').onclick=function(){var set={},bad=null;FIELDS.forEach(function(d){var e=$('#f_'+d[0]);
if(d[1]==='bool')set[d[0]]=e.checked;else if(d[1]==='list')set[d[0]]=e.value.split(/[ ,]+/).filter(Boolean).map(Number);else{var n=parseFloat(e.value);if(isNaN(n))bad=d[2];set[d[0]]=n}});
if(bad){status(bad+' is not a number','bad');return}
api('api/mod-store',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({mod:MOD,set:{config:set}})}).then(function(){status('Saved. Applies within 2 seconds, no reload.','ok')}).catch(function(e){status(e.message,'bad')})};
$('#reload').onclick=function(){load().then(function(){status('')})};
load();
})();
""";

    /// <summary>
    /// <c>GET /auction</c>: the auction house rules (durations, fee, cut, limits, allow_special, expiry interval) of the auction_house mod, edited while the
    /// server runs: reads the mod's <c>rules</c> endpoint and writes its storage key <c>config</c> through <c>POST /api/mod-store</c>.
    /// </summary>
    public static string AuctionPage(IServerAdmin admin)
    {
        var o = admin.Overview();
        var sb = new StringBuilder(6 * 1024);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<meta name=\"color-scheme\" content=\"dark light\"><script>try{var m=localStorage.getItem('gsTheme');if(m==='light'||m==='dark')document.documentElement.dataset.theme=m;}catch(e){}</script><title>Auction - ")
          .Append(E(o.Name)).Append(" - Sacred GameServer</title><style>").Append(Style).Append(AuctionCss).Append("</style></head><body><div class=\"app\">");
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"logo\">").Append(Icon("shield")).Append("</span><div><b>Sacred GameServer</b><small>")
          .Append(E(o.Name)).Append("</small></div></div>");
        Tabs(sb, "auction", admin.Mods().Count);
        sb.Append("</header><main class=\"au\"><section class=\"card\" id=\"missing\" hidden><h2>Auction house</h2><p>The <code>auction_house</code> mod is not loaded (or too old for this page). Copy <code>auction_house.lua</code> from <code>scripts-examples</code> into the scripts folder.</p><p class=\"dim small\" id=\"why\"></p></section>")
          .Append("<section class=\"card\" id=\"main\" hidden><h2>Auction house rules</h2><div id=\"form\"></div>")
          .Append("<div class=\"row\" style=\"margin-top:.8rem\"><button type=\"button\" class=\"primary\" id=\"save\">Save</button><button type=\"button\" class=\"ghost\" id=\"reload\">Refresh</button></div>")
          .Append("<p class=\"hint dim small\">Stored in the mod's storage (key <code>config</code>); a change applies within 2 seconds, no restart or reload. The script file's values only seed a fresh install.</p>")
          .Append("<p class=\"status\" id=\"status\" aria-live=\"polite\"></p></section></main></div><script>").Append(AuctionScript).Append("</script></body></html>");
        return sb.ToString();
    }
}
