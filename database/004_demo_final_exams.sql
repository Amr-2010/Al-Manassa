-- Demo final exams: 20 distinct scenario questions per demo course.
-- Seeded into PostgreSQL, idempotent; editorial review required before production certification.
WITH base AS (
 SELECT c.id AS course_id, g.n,
 CASE c.category
 WHEN 'Technology' THEN (ARRAY['تحديد الهدف','جودة البيانات','تقسيم البيانات','اختيار المقياس','التعلم المراقب','التعلم غير المراقب','التحيز','الخصوصية','التقييم البشري','التسرب','النشر','المراقبة','التصنيف','الانحدار','التعميم','التوثيق','التجربة','المخاطر','الشفافية','التحسين'])[g.n]
 WHEN 'Programming' THEN (ARRAY['أنواع C#','التحقق من المدخلات','GET','POST','حقن الاعتماديات','المعاملات','المفاتيح الأجنبية','الاستثناءات','اختبارات الوحدة','التسجيل','أسرار البيئة','المصادقة','التفويض','الترحيلات','HTTPS','async await','JSON','REST','مراقبة الصحة','النشر'])[g.n]
 ELSE (ARRAY['الجمهور','القيمة المقترحة','الهدف','المحتوى','البريد','الموافقة','SEO','معدل التحويل','تكلفة الاكتساب','اختبار A/B','صفحة الهبوط','قنوات التوزيع','القياس','التقسيم','الميزانية','رسالة العلامة','رحلة العميل','الاحتفاظ','التحليلات','التحسين'])[g.n] END AS topic
 FROM courses c CROSS JOIN generate_series(1,20) g(n)
 WHERE c.id IN ('11111111-1111-4111-8111-111111111111','22222222-2222-4222-8222-222222222222','33333333-3333-4333-8333-333333333333')
), new_questions AS (
 INSERT INTO course_questions(id,course_id,stage_id,prompt,position,is_final)
 SELECT gen_random_uuid(),b.course_id,NULL,'في سياق '||b.topic||'، ما أفضل طريقة لاتخاذ قرار مهني سليم؟ (حالة تدريبية '||b.n||')',b.n,true
 FROM base b WHERE NOT EXISTS(SELECT 1 FROM course_questions q WHERE q.course_id=b.course_id AND q.is_final AND q.position=b.n)
 RETURNING id,course_id,position
)
INSERT INTO question_options(id,question_id,label,is_correct)
SELECT gen_random_uuid(),q.id,answer.label,answer.pos=1
FROM course_questions q JOIN base b ON b.course_id=q.course_id AND b.n=q.position
CROSS JOIN LATERAL (VALUES
 (1,'تحديد الهدف وفحص الأدلة ذات الصلة بموضوع '||b.topic),
 (2,'الاعتماد على التخمين دون قياس'),
 (3,'تجاهل الخصوصية والتحقق'),
 (4,'تعميم نتيجة واحدة دون اختبار')) answer(pos,label)
WHERE q.is_final AND q.stage_id IS NULL
AND NOT EXISTS(SELECT 1 FROM question_options o WHERE o.question_id=q.id);
