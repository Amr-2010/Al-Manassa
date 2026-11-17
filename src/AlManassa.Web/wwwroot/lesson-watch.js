(function(){
 const active=new Map();
 function youtubeReady(){
  if(window.YT && window.YT.Player)return Promise.resolve();
  if(!window._alYTLoad)window._alYTLoad=new Promise((resolve,reject)=>{
   const old=window.onYouTubeIframeAPIReady;
   window.onYouTubeIframeAPIReady=()=>{if(typeof old==="function")old();resolve();};
   const script=document.createElement("script");script.src="https://www.youtube.com/iframe_api";
   script.onerror=()=>reject(new Error("YouTube player API unavailable"));document.head.appendChild(script);
  });
  return window._alYTLoad;
 }
 window.alManassaWatch={attach:async function(dotnet,lessons){
  for(const item of lessons){
   const id=item.id,el=document.getElementById("lesson-video-"+id);
   if(!el)continue;
   const existing=active.get(id);
   if(existing && existing.el===el){existing.dotnet=dotnet;continue;}
   if(existing){try{existing.player?.destroy();}catch(_){}active.delete(id);}
   const entry={el,dotnet,ended:false,maxTime:0};active.set(id,entry);
   const finish=()=>{if(entry.ended)return;entry.ended=true;entry.dotnet.invokeMethodAsync("VideoEnded",id).catch(()=>{entry.ended=false;});};
   if(item.provider==="hosted"){
    el.addEventListener("timeupdate",()=>{
     if(!Number.isFinite(el.duration)||el.duration<=0)return;
     if(el.currentTime>entry.maxTime+1.5){el.currentTime=entry.maxTime;return;}
     entry.maxTime=Math.max(entry.maxTime,el.currentTime);
    });
    el.addEventListener("seeking",()=>{
     if(el.currentTime>entry.maxTime+1){el.currentTime=entry.maxTime;}
    });
    el.addEventListener("ended",()=>{if(el.duration>0&&entry.maxTime>=el.duration-2)finish();});
   }else if(item.provider==="youtube"){
    await youtubeReady();
    if(!document.getElementById("lesson-video-"+id)){active.delete(id);continue;}
    entry.player=new YT.Player(el,{events:{
     onReady:event=>{entry.player=event.target;entry.maxTime=0;},
     onStateChange:event=>{
      if(event.data===YT.PlayerState.PLAYING){
       if(!entry.timer)entry.timer=setInterval(()=>{
        try{
         const t=entry.player.getCurrentTime(),duration=entry.player.getDuration();
         if(t>entry.maxTime+2){entry.player.seekTo(entry.maxTime,true);return;}
         entry.maxTime=Math.max(entry.maxTime,t);
        }catch(_){}
       },400);
      }
      if(event.data===YT.PlayerState.ENDED){
       const duration=entry.player.getDuration();
       if(duration>0&&entry.maxTime>=duration-2)finish();
      }
     }
    }});
   }
  }
 }};
})();