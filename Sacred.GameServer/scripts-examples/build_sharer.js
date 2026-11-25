var q=document.getElementById('q'),c=document.getElementById('c'),n=document.getElementById('n');
function f(){var t=q.value.toLowerCase(),k=c.value,s=0,all=document.querySelectorAll('.card');
for(var i=0;i<all.length;i++){var e=all[i],ok=(!t||e.getAttribute('data-s').indexOf(t)>=0)&&(!k||e.getAttribute('data-c')===k);e.style.display=ok?'':'none';if(ok)s++;}
n.textContent=s+' shown';}
q.oninput=f;c.onchange=f;f();