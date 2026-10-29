CREATE TABLE users (id INT PRIMARY KEY, name TEXT, age INT);
INSERT INTO users VALUES (1, 'Анна', 25), (2, 'Иван', 31);
SELECT name FROM users WHERE age > 20 ORDER BY name;
CREATE INDEX idx_age ON users(age);
EXPLAIN SELECT * FROM users WHERE age = 31;
SELECT * FROM users WHERE age = 31;
