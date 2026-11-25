(function(){
'use strict';
var $=function(id){return document.getElementById(id);};
var chat=$('chat'),latest=$('latest'),live=$('live'),ask=$('ask');
function atBottom(){return chat.scrollTop+chat.clientHeight>=chat.scrollHeight-40;}
function toBottom(){chat.style.scrollBehavior='auto';chat.scrollTop=chat.scrollHeight;chat.style.scrollBehavior='';latest.hidden=true;}
toBottom();chat.addEventListener('scroll',function(){if(atBottom())latest.hidden=true;});latest.onclick=toBottom;
var t=$('toast');if(t){history.replaceState(null,'',location.pathname);}

// confirm / ask-for-reason dialog on forms that carry data-title
var pending=null;
document.addEventListener('submit',function(e){var f=e.target;if(!f.dataset||!f.dataset.title||f.dataset.ok)return;
e.preventDefault();pending=f;$('askTitle').textContent=f.dataset.title;$('askText').textContent=f.dataset.text||'';
var r=$('askReason');r.hidden=!('askReason' in f.dataset);r.value='';ask.showModal();if(!r.hidden)r.focus();});
ask.addEventListener('close',function(){var f=pending;pending=null;if(!f||ask.returnValue!=='ok')return;
var r=f.querySelector('input[name=reason]');if(r&&'askReason' in f.dataset)r.value=$('askReason').value;f.dataset.ok='1';f.requestSubmit();});
$('askReason').addEventListener('keydown',function(e){if(e.key==='Enter'){e.preventDefault();$('askOk').click();}});

// theme: auto (follows the system) -> light -> dark, remembered in this browser
var themeBtn=$('theme');
var themeIcons={auto:'<svg class="i" viewBox="0 0 24 24"><path d="M2 4h20v13H2zM8 21h8M12 17v4"/></svg>',light:'<svg class="i" viewBox="0 0 24 24"><path d="M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10zM12 1v2M12 21v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M1 12h2M21 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4"/></svg>',dark:'<svg class="i" viewBox="0 0 24 24"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>'};
var themeNames={auto:'Auto',light:'Light',dark:'Dark'};
var theme=(function(){try{var m=localStorage.getItem('gsTheme');return m==='light'||m==='dark'?m:'auto';}catch(e){return 'auto';}})();
function applyTheme(){if(theme==='auto')delete document.documentElement.dataset.theme;else document.documentElement.dataset.theme=theme;
try{if(theme==='auto')localStorage.removeItem('gsTheme');else localStorage.setItem('gsTheme',theme);}catch(e){}
themeBtn.innerHTML=themeIcons[theme]+'<span>'+themeNames[theme]+'</span>';themeBtn.title='Theme: '+themeNames[theme]+' (click to change)';}
themeBtn.addEventListener('click',function(){theme=theme==='auto'?'light':theme==='light'?'dark':'auto';applyTheme();});applyTheme();

// "message" button next to a player
document.addEventListener('click',function(e){var b=e.target.closest&&e.target.closest('[data-msg]');if(!b)return;$('chatTo').value=b.dataset.msg;$('chatText').focus();});

// chat
function line(e){var d=document.createElement('div');d.className='msg k'+e.kind;d.dataset.id=e.id;d.style.setProperty('--h',(e.fromSlot*47)%360);
var tm=document.createElement('time');tm.textContent=new Date(e.timeUnixMs).toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'});d.append(tm);
function who(n){var b=document.createElement('b');b.textContent=n;d.append(b);}
function to(s){var m=document.createElement('em');m.textContent='→ #'+s;d.append(m);}
switch(e.kind){case 0:who(e.fromName);break;case 1:who(e.fromName);to(e.toSlot);break;case 2:who('Server');break;case 3:who('Server');to(e.toSlot);break;default:if(e.fromName)who(e.fromName);}
var x=document.createElement('span');x.className='text';x.textContent=e.text;d.append(x);return d;}
function add(e){var stick=atBottom();chat.append(line(e));while(chat.childElementCount>500)chat.firstElementChild.remove();if(stick)toBottom();else latest.hidden=false;}
var last=chat.lastElementChild?chat.lastElementChild.dataset.id:'0';
var es=new EventSource('api/chat/stream?after='+last);
es.addEventListener('chat',function(m){add(JSON.parse(m.data));});
es.onopen=function(){live.textContent='live';live.className='live on';};es.onerror=function(){live.textContent='reconnecting';live.className='live';};
$('chatForm').addEventListener('submit',function(e){e.preventDefault();var f=e.target;
fetch(f.action,{method:'POST',headers:{'Accept':'application/json'},body:new URLSearchParams(new FormData(f))}).then(function(r){if(r.ok){f.text.value='';f.text.focus();}});});

// Lock/pause flip at once (the markup of the other state is in data-alt), stay disabled until the server confirms, and the page
// polls every 250 ms meanwhile. The swap leaves #session alone while a flip is unconfirmed (max 5 s, then the server's state wins).
var fastUntil=0,flips={};
function fast(){fastUntil=Date.now()+5000;}
if(t)fast();
function sessionBusy(src){var busy=false,now=Date.now();Object.keys(flips).forEach(function(a){var f=src.querySelector('form[action="actions/'+a+'"] input[name=value]');
if(f&&f.value===flips[a].expected){delete flips[a];}else if(now-flips[a].since>5000){delete flips[a];toast('The server did not confirm the change.');}else busy=true;});return busy;}
function toast(text){var d=document.createElement('div');d.id='toast';d.setAttribute('role','status');d.textContent=text;document.body.append(d);setTimeout(function(){d.remove();},3600);}
document.addEventListener('submit',function(e){var f=e.target,a=f.getAttribute&&f.getAttribute('action');if(a!=='actions/lock'&&a!=='actions/pause')return;
e.preventDefault();var b=f.querySelector('button.tile'),h=f.querySelector('input[name=value]');if(b.disabled)return;
var body=new URLSearchParams(new FormData(f));var alt=b.dataset.alt;b.dataset.alt=b.innerHTML;b.innerHTML=alt;b.classList.toggle('active');b.classList.add('pending');b.disabled=true;
var key=a.slice(8);h.value=h.value==='1'?'0':'1';flips[key]={expected:h.value,since:Date.now()};
fetch(a,{method:'POST',headers:{'Accept':'application/json'},body:body}).then(function(r){if(!r.ok)throw new Error(r.status);fast();refresh();}).catch(function(){delete flips[key];toast('The change was refused.');refresh();});});

// swap the live blocks without disturbing an input the user is typing in or a form with unsaved edits
document.addEventListener('input',function(e){if(e.target.form)e.target.form.dataset.dirty='1';});
function refresh(){if(ask.open)return Promise.resolve();return fetch('fragment',{cache:'no-store'}).then(function(r){return r.ok?r.text():null;}).then(function(h){if(!h)return;
var doc=new DOMParser().parseFromString(h,'text/html');
['head','players','session','flood','bans'].forEach(function(id){var cur=$(id),src=doc.getElementById(id);if(id==='session'&&src&&sessionBusy(src))return;if(!cur||!src||cur.contains(document.activeElement)||cur.querySelector('form[data-dirty]'))return;if(cur.innerHTML!==src.innerHTML)cur.innerHTML=src.innerHTML;});
var sel=$('chatTo'),v=sel.value,opts=doc.querySelectorAll('#players [data-msg]');
var slots=Array.prototype.map.call(opts,function(o){return o.dataset.msg;});var have=Array.prototype.map.call(sel.options,function(o){return o.value;}).slice(1);
if(slots.join()!==have.join()){while(sel.options.length>1)sel.remove(1);Array.prototype.forEach.call(doc.querySelectorAll('#players .player'),function(p){var s=p.querySelector('[data-msg]').dataset.msg;sel.add(new Option('#'+s+' '+p.querySelector('.who b').textContent,s));});sel.value=slots.indexOf(v)>=0||v==='0'?v:'0';}
}).catch(function(){});}
function tick(){refresh().then(function(){setTimeout(tick,Date.now()<fastUntil?250:3000);});}
tick();
})();
