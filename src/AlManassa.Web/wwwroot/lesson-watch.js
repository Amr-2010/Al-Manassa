(function(){
 const active=new Map();let ytLoading=false;
 function youtubeReady(){
  if(window.YT&&window.YT.Player)return Promise.resolve();
  if(!window._alYTLoad){
   window._alYTLoad=new Promise(resolve=>{
    const old=window.onYouTubeIframeAPIReady;
    window.onYouTubeIframeAPIReady=()=>{if(typeof old==="function")old();resolve();};
    const script=document.createElement("script");script.src="https://www.youtube.com/iframe_api";document.head.appendChild(script);
   });
  }
  return window._alYTLoad;
 }
 window.alManassaWatch={attach:async function(dotnet,lessons){
  for(const item of lessons){
   if(active.has(item.id))continue;
   const el=document.getElementById("lesson-video-"+item.id);if(!el)continue;
   const entry={el,ended:false};active.set(item.id,entry);
   const finish=()=>{if(entry.ended)return;entry.ended=true;dotnet.invokeMethodAsync("VideoEnded",item.id).catch(()=>{});};
   if(item.provider==="hosted"){
    el.addEventListener("ended",finish,{once:true});
   }else if(item.provider==="youtube"){
    await youtubeReady();
    if(!document.getElementById("lesson-video-"+item.id)){active.delete(item.id);continue;}
    entry.player=new YT.Player(el,{events:{onStateChange:event=>{
     if(event.data===YT.PlayerState.ENDED)finish();
    }}});
   }
  }
 }};
})();