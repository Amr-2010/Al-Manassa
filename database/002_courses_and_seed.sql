-- Apply only to a dedicated Al-Manassa PostgreSQL database, NEVER to Xerox/SakanGo.
CREATE TABLE IF NOT EXISTS courses (
 id uuid PRIMARY KEY,
 title_ar varchar(250) NOT NULL,
 title_en varchar(250) NOT NULL,
 description text,
 category varchar(100) NOT NULL DEFAULT 'General',
 duration_weeks integer NOT NULL DEFAULT 4 CHECK(duration_weeks BETWEEN 1 AND 52),
 published boolean NOT NULL DEFAULT false,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS curriculum_suggestions (
 id uuid PRIMARY KEY,
 topic varchar(250) NOT NULL,
 level varchar(40) NOT NULL,
 language varchar(20) NOT NULL,
 duration_weeks integer NOT NULL,
 content_json jsonb NOT NULL,
 provider varchar(80) NOT NULL,
 status varchar(30) NOT NULL DEFAULT 'PendingReview',
 created_at timestamptz NOT NULL DEFAULT now()
);
-- Idempotent demo seed, saved in PostgreSQL (not hardcoded in Razor).
INSERT INTO courses(id,title_ar,title_en,description,category,duration_weeks,published) VALUES
('11111111-1111-4111-8111-111111111111','أساسيات الذكاء الاصطناعي','AI Fundamentals','تعرف على مفاهيم وتطبيقات الذكاء الاصطناعي.','Technology',8,true),
('22222222-2222-4222-8222-222222222222','تطوير تطبيقات .NET','Developing with .NET','تعلم تطوير تطبيقات حديثة باستخدام .NET.','Programming',6,true),
('33333333-3333-4333-8333-333333333333','التسويق الرقمي','Digital Marketing','أساسيات التسويق عبر القنوات الرقمية.','Business',4,true)
ON CONFLICT (id) DO NOTHING;
