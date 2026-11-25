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
