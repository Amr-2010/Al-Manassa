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
  Return a JSON object with title, overview, stages, final_exam.
  Each stage must contain title, objectives, at least 3 substantial lessons with title, content, exercise, video_search_query, and at least 5 multiple-choice quiz questions.
  The final_exam must have exactly 20 distinct, subject-specific multiple-choice questions.
  Each question has prompt and exactly four options with label and is_correct; exactly one correct option.
  Ensure educational accuracy, meaningful explanations, increasing difficulty, no generic repeated questions.
  video_search_query should be a targeted YouTube search phrase; NEVER invent a video ID or URL.
  This is an editorial draft, not an accredited course. No automatic publication.
  """;
  var model=config["GEMINI_MODEL"]??"gemini-2.5-flash";
  if(!System.Text.RegularExpressions.Regex.IsMatch(model,@"^[a-zA-Z0-9._-]+$"))throw new InvalidOperationException("Invalid Gemini model");
  using var req=new HttpRequestMessage(HttpMethod.Post,"https://generativelanguage.googleapis.com/v1beta/models/"+model+":generateContent");
  req.Headers.Add("x-goog-api-key",key);
  req.Content=JsonContent.Create(new {
   contents=new[]{new{parts=new[]{new{text=prompt}}}},
   generationConfig=new{responseMimeType="application/json",maxOutputTokens=8192}
  });
  using var response=await factory.CreateClient("gemini").SendAsync(req);
  if(!response.IsSuccessStatusCode){
   if((int)response.StatusCode==429)throw new InvalidOperationException("تم الوصول إلى حد الاستخدام المجاني لـ Gemini. حاول لاحقًا.");
   throw new InvalidOperationException("Gemini API error: HTTP "+(int)response.StatusCode);
  }
  using var outer=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
  var raw=outer.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()??throw new InvalidOperationException("Empty Gemini response");
  using var doc=JsonDocument.Parse(raw);
  var root=doc.RootElement;
  if(!root.TryGetProperty("stages",out var stages)||stages.ValueKind!=JsonValueKind.Array||stages.GetArrayLength()==0)throw new InvalidOperationException("Missing stages");
  if(!root.TryGetProperty("final_exam",out var exam)||exam.ValueKind!=JsonValueKind.Array||exam.GetArrayLength()!=20)throw new InvalidOperationException("Final exam must contain 20 questions");
  var id=Guid.NewGuid();
  await store.SaveSuggestionAsync(id,topic,level,language,weeks,raw,"Gemini");
  return id;
 }
}
