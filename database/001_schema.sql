-- Starter schema: execute against PostgreSQL after creating a database.
CREATE TABLE IF NOT EXISTS users (id uuid PRIMARY KEY, email varchar(320) NOT NULL UNIQUE, display_name varchar(200) NOT NULL, role varchar(30) NOT NULL, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS courses (id uuid PRIMARY KEY, title_ar varchar(250) NOT NULL, title_en varchar(250) NOT NULL, description text, published boolean NOT NULL DEFAULT false);
CREATE TABLE IF NOT EXISTS enrollments (user_id uuid NOT NULL REFERENCES users(id), course_id uuid NOT NULL REFERENCES courses(id), enrolled_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(user_id,course_id));
