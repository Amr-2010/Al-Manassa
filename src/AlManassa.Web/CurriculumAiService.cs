using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace AlManassa.Web;
public sealed class CurriculumAiService(IConfiguration config,IHttpClientFactory factory,CourseStore store) {
 public async Task<Guid> GenerateAsync(string topic,string level,string language,int weeks) {
  if(string.IsNullOrWhiteSpace(topic)||topic.Length>250||weeks is <1 or >24)throw new ArgumentException("Invalid course request");
  var key=config["OPENAI_API_KEY"];
  if(string.IsNullOrWhiteSpace(key))throw new InvalidOperationException("OPENAI_API_KEY is not configured");
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
  using var req=new HttpRequestMessage(HttpMethod.Post,"v1/chat/completions");
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
  req.Content=JsonContent.Create(new {model=config["OPENAI_MODEL"]??"gpt-4.1-mini",messages=new[]{new{role="system",content="You are an expert instructional designer. Respond with valid JSON only."},new{role="user",content=prompt}},response_format=new{type="json_object"}});
  using var response=await factory.CreateClient("ai").SendAsync(req);
  if(!response.IsSuccessStatusCode)throw new InvalidOperationException("AI provider request failed");
  using var outer=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
  var raw=outer.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()??throw new InvalidOperationException("Empty AI response");
  using var doc=JsonDocument.Parse(raw);
  var root=doc.RootElement;
  if(!root.TryGetProperty("stages",out var stages)||stages.ValueKind!=JsonValueKind.Array||stages.GetArrayLength()==0)throw new InvalidOperationException("Missing stages");
  if(!root.TryGetProperty("final_exam",out var exam)||exam.ValueKind!=JsonValueKind.Array||exam.GetArrayLength()!=20)throw new InvalidOperationException("Final exam must contain 20 questions");
  var id=Guid.NewGuid();
  await store.SaveSuggestionAsync(id,topic,level,language,weeks,raw,"OpenAI");
  return id;
 }
}
