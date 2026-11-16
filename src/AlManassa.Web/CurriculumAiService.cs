using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace AlManassa.Web;
public sealed class CurriculumAiService(IConfiguration config,IHttpClientFactory factory,CourseStore store) {
 public async Task<Guid> GenerateAsync(string topic,string level,string language,int weeks) {
  if(string.IsNullOrWhiteSpace(topic)||topic.Length>250||weeks is <1 or >24)throw new ArgumentException("Invalid course request");
  var key=config["GEMINI_API_KEY"];
  if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("GEMINI_API_KEY is not configured");
  var prompt=$"""
  Design a complete original {weeks}-week {level} course on {topic} in {language}.
  Return a JSON object with title, overview, lessons, final_exam.
  Provide 3 to 6 ordered lessons directly under lessons (no stages or units). Each lesson has title, content, exercise, video_search_query, and exactly 2 multiple-choice quiz questions.
  The final_exam must have exactly 20 distinct, subject-specific multiple-choice questions.
  Each question has prompt and exactly four options with label and is_correct; exactly one correct option.
  Ensure educational accuracy, meaningful explanations, increasing difficulty, no generic repeated questions.
  video_search_query should be a targeted YouTube search phrase; NEVER invent a video ID or URL.
  This is an editorial draft, not an accredited course. No automatic publication.
  """;
  var client=factory.CreateClient("gemini");
  var configured=config["GEMINI_MODEL"];
  var model=string.IsNullOrWhiteSpace(configured)?"gemini-2.5-flash":configured.Trim();
  if(!System.Text.RegularExpressions.Regex.IsMatch(model,@"^[a-zA-Z0-9._-]+$"))throw new InvalidOperationException("Invalid Gemini model");
  async Task<HttpResponseMessage> RequestAsync(string selectedModel){
   using var request=new HttpRequestMessage(HttpMethod.Post,"https://generativelanguage.googleapis.com/v1beta/models/"+selectedModel+":generateContent");
   request.Headers.Add("x-goog-api-key",key);
   request.Content=JsonContent.Create(new {
    contents=new[]{new{parts=new[]{new{text=prompt}}}},
    generationConfig=new{responseMimeType="application/json",maxOutputTokens=16384}
   });
   return await client.SendAsync(request);
  }
  using var response=await RequestAsync(model);
  if(!response.IsSuccessStatusCode){
   var errorBody=await response.Content.ReadAsStringAsync();
   string detail="";
   try{
    using var errorJson=JsonDocument.Parse(errorBody);
    detail=errorJson.RootElement.GetProperty("error").GetProperty("message").GetString()??"";
   }catch(JsonException){}
   if((int)response.StatusCode==429)throw new InvalidOperationException("تم الوصول إلى حد الاستخدام المتاح لـ Gemini. حاول لاحقًا.");
   if((int)response.StatusCode==404){
    using var listRequest=new HttpRequestMessage(HttpMethod.Get,"https://generativelanguage.googleapis.com/v1beta/models?pageSize=100");
    listRequest.Headers.Add("x-goog-api-key",key);
    using var listResponse=await client.SendAsync(listRequest);
    if(listResponse.IsSuccessStatusCode){
     using var available=JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
     var names=new List<string>();
     if(available.RootElement.TryGetProperty("models",out var models)){
      foreach(var item in models.EnumerateArray()){
       if(item.TryGetProperty("supportedGenerationMethods",out var methods)&&methods.EnumerateArray().Any(x=>x.GetString()=="generateContent")
        &&item.TryGetProperty("name",out var name))names.Add(name.GetString()!.Replace("models/",""));
      }
     }
     var suggestion=names.FirstOrDefault(x=>x.Contains("flash",StringComparison.OrdinalIgnoreCase))??names.FirstOrDefault();
     throw new InvalidOperationException("النموذج "+model+" غير متاح لهذا المفتاح. "+(suggestion is null?"لم تُرجع Google نموذجًا متاحًا.":"جرّب ضبط GEMINI_MODEL على "+suggestion)+" — "+detail);
    }
   }
   throw new InvalidOperationException("Gemini HTTP "+(int)response.StatusCode+": "+(detail.Length>350?detail[..350]:detail));
  }
  using var outer=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
  var raw=string.Concat(outer.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts").EnumerateArray().Select(x=>x.TryGetProperty("text",out var t)?t.GetString():""));
  if(string.IsNullOrWhiteSpace(raw))throw new InvalidOperationException("Empty Gemini response");
  using var doc=JsonDocument.Parse(raw);
  var root=doc.RootElement;
  if(!root.TryGetProperty("lessons",out var lessons)||lessons.ValueKind!=JsonValueKind.Array||lessons.GetArrayLength()==0)throw new InvalidOperationException("Missing lessons");
  if(!root.TryGetProperty("final_exam",out var exam)||exam.ValueKind!=JsonValueKind.Array||exam.GetArrayLength()!=20)throw new InvalidOperationException("Final exam must contain 20 questions");
  var id=Guid.NewGuid();
  await store.SaveSuggestionAsync(id,topic,level,language,weeks,raw,"Gemini");
  return id;
 }
}
