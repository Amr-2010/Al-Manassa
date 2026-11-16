-- Al-Manassa: database-owned lesson media and per-student playback position.
-- Apply after the application's existing schema; safe to rerun.
CREATE TABLE IF NOT EXISTS lesson_assets (
 id uuid PRIMARY KEY,
 lesson_id uuid NOT NULL REFERENCES stage_lessons(id) ON DELETE CASCADE,
 filename varchar(255) NOT NULL,
 mime_type varchar(100) NOT NULL,
 bytes bytea NOT NULL,
 size_bytes integer NOT NULL CHECK(size_bytes BETWEEN 1 AND 10485760),
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_lesson_assets_lesson ON lesson_assets(lesson_id);
CREATE TABLE IF NOT EXISTS student_lesson_progress (
 user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
 lesson_id uuid NOT NULL REFERENCES stage_lessons(id) ON DELETE CASCADE,
 playback_seconds integer NOT NULL DEFAULT 0 CHECK(playback_seconds>=0),
 last_position_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(user_id,lesson_id)
);
-- Existing column CHECK only permits youtube/hosted; support image/link.
ALTER TABLE stage_lessons DROP CONSTRAINT IF EXISTS stage_lessons_video_provider_check;
ALTER TABLE stage_lessons ADD CONSTRAINT stage_lessons_video_provider_check
 CHECK(video_provider IS NULL OR video_provider IN ('youtube','hosted','image','link'));
