(function(){
 const active=new Map();
 function youtubeReady(){
  if(window.YT&&window.YT.Player)return Promise.resolve();
  if(!window._alYTLoad)window._alYTLoad=new Promise((resolve,reject)=>{
   const old=window.onYouTubeIframeAPIReady;
   window.onYouTubeIframeAPIReady=()=>{if(typeof old==="function")old();resolve();};
   const script=document.createElement("script");script.src="https://www.youtube.com/iframe_api";
   script.onerror=()=>reject(new Error("YouTube API unavailable"));document.head.appendChild(script);
  });
  return window._alYTLoad;
 }
 function progress(entry,force){
  const now=Date.now();
  if(!force&&now-entry.lastSave<10000)return;
  const duration=Math.round(entry.duration||0);
  if(duration<1)return;
  entry.lastSave=now;
  entry.dotnet.invokeMethodAsync("VideoProgress",entry.id,Math.floor(entry.position),Math.floor(entry.maxTime),duration).catch(()=>{});
 }
 function advance(entry,t,duration){
  if(!Number.isFinite(t)||!Number.isFinite(duration)||duration<=0)return false;
  entry.duration=duration;
  if(t>entry.maxTime+2){
   if(entry.provider==="hosted")entry.el.currentTime=entry.maxTime;
   else entry.player?.seekTo(entry.maxTime,true);
   return false;
  }
  entry.position=t;
  entry.maxTime=Math.max(entry.maxTime,t);
  progress(entry,false);
  return true;
 }
 function finish(entry){
  if(entry.ended||!entry.duration||entry.maxTime<entry.duration-2)return;
  entry.ended=true;
  entry.position=entry.duration;entry.maxTime=entry.duration;
  progress(entry,true);
  entry.dotnet.invokeMethodAsync("VideoEnded",entry.id).catch(()=>{entry.ended=false;});
 }
 window.alManassaWatch={attach:async function(dotnet,lessons){
  for(const item of lessons){
   const id=item.id,el=document.getElementById("lesson-video-"+id);
   if(!el)continue;
   const old=active.get(id);
   if(old&&(old.el===el||old.player?.getIframe?.()===el)){old.dotnet=dotnet;continue;}
   if(old){if(old.timer)clearInterval(old.timer);try{old.player?.destroy();}catch(_){}active.delete(id);}
   const entry={id,el,dotnet,provider:item.provider,maxTime:Math.max(0,item.watched||0),position:Math.max(0,item.position||0),duration:0,lastSave:0,ended:false};
   active.set(id,entry);
   if(item.provider==="hosted"){
    el.addEventListener("loadedmetadata",()=>{
     entry.duration=el.duration;
     entry.maxTime=Math.min(entry.maxTime,el.duration);
     el.currentTime=Math.min(entry.position,entry.maxTime);
     progress(entry,true);
    });
    el.addEventListener("timeupdate",()=>advance(entry,el.currentTime,el.duration));
    el.addEventListener("seeking",()=>{if(el.currentTime>entry.maxTime+1)el.currentTime=entry.maxTime;});
    el.addEventListener("pause",()=>progress(entry,true));
    el.addEventListener("ended",()=>finish(entry));
   }else if(item.provider==="youtube"){
    await youtubeReady();
    if(!document.getElementById("lesson-video-"+id)){active.delete(id);continue;}
    entry.player=new YT.Player(el,{events:{
     onReady:e=>{entry.player=e.target;entry.duration=entry.player.getDuration();entry.maxTime=Math.min(entry.maxTime,entry.duration||0);entry.player.seekTo(Math.min(entry.position,entry.maxTime),true);},
     onStateChange:e=>{
      if(e.data===YT.PlayerState.PLAYING&&!entry.timer)entry.timer=setInterval(()=>{
       try{advance(entry,entry.player.getCurrentTime(),entry.player.getDuration());}catch(_){}
      },500);
      if(e.data===YT.PlayerState.PAUSED)progress(entry,true);
      if(e.data===YT.PlayerState.ENDED){try{advance(entry,entry.player.getCurrentTime(),entry.player.getDuration());finish(entry);}catch(_){}}
     }
    }});
   }
  }
 }};
})();