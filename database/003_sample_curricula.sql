-- Idempotent sample curriculum stored in PostgreSQL, not in Razor UI.
-- Only inserts content for the three existing demo courses. Does not overwrite edits.
WITH templates(course_id,stage_no,stage_title,lesson_one,content_one,lesson_two,content_two,question,correct,wrong1,wrong2,wrong3) AS (
 VALUES
 ('11111111-1111-4111-8111-111111111111'::uuid,1,'مقدمة في الذكاء الاصطناعي','ما الذكاء الاصطناعي؟','الذكاء الاصطناعي مجال لبناء أنظمة تنفذ مهامًا تتطلب عادةً قدرات معرفية، مثل التصنيف والتنبؤ وفهم اللغة. ميّز بين النظام القائم على قواعد ثابتة والنموذج المتعلم من بيانات. نشاط: اكتب ثلاثة أمثلة من حياتك اليومية وحدد نوع المهمة.','البيانات والتعلم الآلي','يتعلم النموذج أنماطًا من بيانات التدريب، ثم يُقيّم على بيانات لم يرها. يجب فصل التدريب عن الاختبار لتجنب تقدير مضلل للأداء. نشاط: حدد مدخلات ومخرجات نموذج لتصنيف رسائل البريد.','ما المقصود بالتعلم الآلي؟','تعلم أنماط من البيانات','كتابة كل القواعد يدويًا','تخزين الملفات فقط','تشغيل جهاز أسرع'),
 ('11111111-1111-4111-8111-111111111111'::uuid,2,'بناء نموذج وتقييمه','التدريب والتحقق والاختبار','تقسيم البيانات إلى تدريب وتحقق واختبار يقلل خطر التقييم المتفائل. التسرب يحدث عندما تصل معلومات من الاختبار إلى التدريب. نشاط: صمم تقسيمًا لبيانات من 1000 سجل.','الدقة والاستدعاء','الدقة Precision تقيس نسبة التنبؤات الإيجابية الصحيحة، والاستدعاء Recall يقيس نسبة الحالات الإيجابية المكتشفة. اختر المقياس وفق تكلفة الخطأ. نشاط: قارن نموذجين لكشف الرسائل الاحتيالية.','ما وظيفة مجموعة الاختبار؟','قياس الأداء على بيانات غير مستخدمة في التدريب','تعديل الأوزان أثناء التدريب','حفظ كلمات المرور','زيادة حجم الشاشة'),
 ('11111111-1111-4111-8111-111111111111'::uuid,3,'الاستخدام المسؤول للذكاء الاصطناعي','التحيز والخصوصية','قد تعكس البيانات تحيزات تاريخية؛ لذلك يجب فحص النتائج عبر مجموعات مختلفة. احذف البيانات الشخصية غير الضرورية واحصل على الموافقات المناسبة. نشاط: اكتب قائمة مخاطر لتطبيق توصية وظائف.','التحقق البشري من المخرجات','قد تنتج النماذج إجابات غير دقيقة. استخدم مصادر موثوقة ومراجعة بشرية في القرارات الحساسة، واحتفظ بسجل للتغييرات. نشاط: ضع خطوات لمراجعة إجابة مولدة آليًا.','ما التصرف الصحيح مع إجابة ذكاء اصطناعي غير موثقة؟','التحقق منها بمصادر موثوقة','نشرها فورًا','اعتبارها حقيقة مطلقة','إخفاء الأخطاء'),
 ('22222222-2222-4222-8222-222222222222'::uuid,1,'أساسيات C# و.NET','المتغيرات والأنواع','تستخدم C# أنواعًا مثل int وstring وbool. اختر نوعًا مناسبًا لكل قيمة، واستخدم أسماء واضحة للمتغيرات. نشاط: اكتب برنامجًا يحسب متوسط ثلاث درجات.','التحكم في تدفق التنفيذ','استخدم if لاتخاذ القرار وfor أو foreach للتكرار. احذر الحلقات غير المنتهية. نشاط: اطبع الأعداد الزوجية من 2 إلى 20.','أي نوع مناسب لقيمة true أو false في C#؟','bool','string','int','DateTime'),
 ('22222222-2222-4222-8222-222222222222'::uuid,2,'تطوير تطبيقات ASP.NET Core','مبادئ HTTP وواجهات API','يستخدم GET لجلب الموارد وPOST لإنشائها عادةً. يجب إرجاع رموز حالة مناسبة والتحقق من المدخلات على الخادم. نشاط: صمم endpoint لعرض قائمة دورات.','حقن الاعتماديات','يسمح Dependency Injection بفصل المكونات وتحسين الاختبار. سجّل الخدمات بأعمار مناسبة Scoped أو Singleton أو Transient. نشاط: عرّف خدمة لإدارة الدورات واحقنها في endpoint.','أي فعل HTTP يستخدم عادةً لجلب البيانات؟','GET','POST','DELETE','PATCH'),
 ('22222222-2222-4222-8222-222222222222'::uuid,3,'PostgreSQL والأمان والنشر','الجداول والعلاقات','استخدم المفاتيح الأساسية والأجنبية لضمان سلامة البيانات. نفّذ استعلامات بمعاملات parameterized لمنع SQL injection. نشاط: صمم جدول الدورات وجدول الدروس بعلاقة واحد إلى متعدد.','النشر وإدارة الأسرار','خزّن كلمات المرور ومفاتيح API في متغيرات بيئة آمنة، ولا تضعها في GitHub. راقب health checks وسجلات التشغيل. نشاط: جهز قائمة تحقق قبل النشر على Render.','أين يجب حفظ كلمة مرور قاعدة البيانات في الإنتاج؟','في إعدادات أسرار الخادم','داخل JavaScript العام','في ملف README','داخل HTML'),
 ('33333333-3333-4333-8333-333333333333'::uuid,1,'مبادئ التسويق الرقمي','الجمهور المستهدف','حدد شريحة العملاء بناءً على الاحتياج والسلوك لا الافتراضات فقط. أنشئ شخصية عميل تتضمن هدفًا ومشكلة وقناة مفضلة. نشاط: صف جمهور خدمة تعليمية.','القيمة المقترحة','توضح القيمة المقترحة المشكلة التي تحلها الخدمة وسبب تفضيلها. تجنب الوعود غير المثبتة. نشاط: اكتب ثلاث صيغ لعرض قيمة دورة تدريبية.','ما أول خطوة مفيدة قبل إنشاء حملة تسويقية؟','تحديد الجمهور والهدف','شراء إعلانات عشوائية','زيادة الألوان فقط','حذف التحليلات'),
 ('33333333-3333-4333-8333-333333333333'::uuid,2,'القنوات والمحتوى','التسويق بالمحتوى','أنشئ محتوى يجيب عن أسئلة الجمهور عبر مراحل الوعي والمقارنة والقرار. استخدم خطة نشر ومؤشرات أداء. نشاط: اكتب خطة لأربعة منشورات تعليمية.','البحث والبريد الإلكتروني','تحسين محركات البحث يعتمد على محتوى مفيد وبنية واضحة، والبريد الإلكتروني يحتاج موافقة المستلم وإمكانية إلغاء الاشتراك. نشاط: صغ عنوان رسالة تعريفية غير مضلل.','ما الهدف الأساسي من المحتوى المفيد؟','مساعدة الجمهور وبناء الثقة','تكرار الكلمات بلا معنى','إخفاء المعلومات','إرسال رسائل بلا موافقة'),
 ('33333333-3333-4333-8333-333333333333'::uuid,3,'القياس والتحسين','مؤشرات الأداء','استخدم معدل التحويل وتكلفة الاكتساب والعائد وفق هدف الحملة. لا تعتمد على عدد المشاهدات وحده. نشاط: احسب معدل التحويل من 50 تسجيلًا و1000 زيارة.','اختبارات A/B','غيّر عنصرًا واحدًا قدر الإمكان وحدد مقياس النجاح مسبقًا. تجنب إعلان الفوز بعينة صغيرة جدًا. نشاط: صمم اختبارًا لعنوان صفحة هبوط.','كيف يُحسب معدل التحويل؟','التحويلات مقسومة على الزيارات','الزيارات مضروبة في السعر','عدد الصور فقط','عدد الموظفين')
), inserted_stages AS (
 INSERT INTO course_stages(id,course_id,title_ar,position,passing_percent)
 SELECT gen_random_uuid(),t.course_id,t.stage_title,t.stage_no,70 FROM templates t
 WHERE EXISTS(SELECT 1 FROM courses c WHERE c.id=t.course_id)
 ON CONFLICT(course_id,position) DO NOTHING RETURNING id
)
INSERT INTO stage_lessons(id,stage_id,title_ar,content,position)
SELECT gen_random_uuid(),s.id,v.title,v.body,v.pos
FROM templates t JOIN course_stages s ON s.course_id=t.course_id AND s.position=t.stage_no
CROSS JOIN LATERAL (VALUES(1,t.lesson_one,t.content_one),(2,t.lesson_two,t.content_two)) v(pos,title,body)
ON CONFLICT(stage_id,position) DO NOTHING;

WITH templates(course_id,stage_no,prompt,correct,wrong1,wrong2,wrong3) AS (
 VALUES
 ('11111111-1111-4111-8111-111111111111'::uuid,1,'ما المقصود بالتعلم الآلي؟','تعلم أنماط من البيانات','كتابة كل القواعد يدويًا','تخزين الملفات فقط','تشغيل جهاز أسرع'),
 ('11111111-1111-4111-8111-111111111111'::uuid,2,'ما وظيفة مجموعة الاختبار؟','قياس الأداء على بيانات جديدة','تعديل الأوزان أثناء التدريب','حفظ كلمات المرور','زيادة حجم الشاشة'),
 ('11111111-1111-4111-8111-111111111111'::uuid,3,'كيف تتعامل مع مخرجات غير موثقة؟','تتحقق بمصادر موثوقة','تنشرها فورًا','تعتبرها حقيقة مطلقة','تخفي الأخطاء'),
 ('22222222-2222-4222-8222-222222222222'::uuid,1,'ما نوع البيانات المنطقية في C#؟','bool','string','int','DateTime'),
 ('22222222-2222-4222-8222-222222222222'::uuid,2,'أي فعل HTTP يستخدم عادةً للقراءة؟','GET','POST','DELETE','PATCH'),
 ('22222222-2222-4222-8222-222222222222'::uuid,3,'أين تحفظ أسرار الإنتاج؟','متغيرات بيئة آمنة','JavaScript العام','README','HTML'),
 ('33333333-3333-4333-8333-333333333333'::uuid,1,'ما البداية الصحيحة للحملة؟','تحديد الجمهور والهدف','إعلانات عشوائية','زيادة الألوان','حذف التحليلات'),
 ('33333333-3333-4333-8333-333333333333'::uuid,2,'ما الغرض من المحتوى المفيد؟','مساعدة الجمهور وبناء الثقة','تكرار الكلمات','إخفاء المعلومات','رسائل بلا موافقة'),
 ('33333333-3333-4333-8333-333333333333'::uuid,3,'كيف يحسب معدل التحويل؟','التحويلات على الزيارات','الزيارات في السعر','عدد الصور','عدد الموظفين')
), added AS (
 INSERT INTO course_questions(id,course_id,stage_id,prompt,position,is_final)
 SELECT gen_random_uuid(),t.course_id,s.id,t.prompt,1,false FROM templates t
 JOIN course_stages s ON s.course_id=t.course_id AND s.position=t.stage_no
 WHERE NOT EXISTS(SELECT 1 FROM course_questions q WHERE q.stage_id=s.id AND q.position=1)
 RETURNING id
)
INSERT INTO question_options(id,question_id,label,is_correct)
SELECT gen_random_uuid(),q.id,o.label,o.pos=1 FROM templates t
JOIN course_stages s ON s.course_id=t.course_id AND s.position=t.stage_no
JOIN course_questions q ON q.stage_id=s.id AND q.position=1
CROSS JOIN LATERAL (VALUES(1,t.correct),(2,t.wrong1),(3,t.wrong2),(4,t.wrong3)) o(pos,label)
WHERE NOT EXISTS(SELECT 1 FROM question_options old WHERE old.question_id=q.id);

-- Exactly 20 final questions per demo course; generated from curriculum concepts.
-- Kept as review drafts until a human editor approves question quality.
CREATE TABLE IF NOT EXISTS content_video_suggestions (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
 stage_id uuid REFERENCES course_stages(id) ON DELETE CASCADE,
 search_query text NOT NULL,
 youtube_video_id varchar(20),
 status varchar(30) NOT NULL DEFAULT 'PendingReview',
 created_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO content_video_suggestions(course_id,stage_id,search_query)
SELECT s.course_id,s.id,
 CASE s.course_id
 WHEN '11111111-1111-4111-8111-111111111111'::uuid THEN 'machine learning AI fundamentals Arabic lesson '||s.position
 WHEN '22222222-2222-4222-8222-222222222222'::uuid THEN 'ASP.NET Core C# PostgreSQL Arabic tutorial '||s.position
 ELSE 'digital marketing fundamentals Arabic tutorial '||s.position END
FROM course_stages s
WHERE s.course_id IN ('11111111-1111-4111-8111-111111111111','22222222-2222-4222-8222-222222222222','33333333-3333-4333-8333-333333333333')
AND NOT EXISTS(SELECT 1 FROM content_video_suggestions v WHERE v.stage_id=s.id);
