# MiniDb — учебная мини-СУБД на C# / .NET 8

Готовое консольное приложение: собственный SQL-парсер, бинарные страницы 4096 байт, B-дерево, транзакции и WAL. Готовые СУБД и библиотеки парсинга не используются. Внешние пакеты нужны только для xUnit и BenchmarkDotNet.

## Запуск на Windows с нуля

1. Установите **.NET 8 SDK** с https://dotnet.microsoft.com/download/dotnet/8.0 (именно SDK, не только Runtime).
2. Установите VS Code и расширение **C# Dev Kit** от Microsoft.
3. Откройте папку репозитория через «Файл → Открыть папку». В ней должен быть `MiniDb.sln`.
4. Откройте «Терминал → Создать терминал» (PowerShell). Выполните:

```powershell
dotnet --version
dotnet restore
dotnet build
dotnet test
dotnet run --project src/MiniDb -- --db my.db
```

Появится приглашение `db>`. SQL заканчивается `;`, один запрос может занимать несколько строк. Несколько запросов на одной строке тоже допустимы. `.help` — помощь, `.exit` / `.quit` / `exit` — выход. Незавершённая транзакция при выходе откатывается. Русские строки сохраняются в UTF-8; используйте современный терминал VS Code.

При ошибке SQL программа пишет сообщение и продолжает работу. Позиции в ошибках парсера отсчитываются с нуля от начала запроса. Чтобы увидеть исходники, начните с `Sql.cs`, затем `Engine.cs`; краткие пояснения есть в [архитектуре](docs/architecture.md).

## Обязательный пример

В новой базе выполните:

```sql
CREATE TABLE users (id INT PRIMARY KEY, name TEXT, age INT);
INSERT INTO users VALUES (1, 'Анна', 25), (2, 'Иван', 31);
SELECT name FROM users WHERE age > 20 ORDER BY name;
CREATE INDEX idx_age ON users(age);
EXPLAIN SELECT * FROM users WHERE age = 31;
SELECT * FROM users WHERE age = 31;
```

EXPLAIN покажет `Index seek idx_age (B-tree equality)`. До создания индекса тот же запрос покажет `Full scan`. SELECT вернёт Ивана.

SQL-файл (выберите ещё не существующую базу):

```powershell
dotnet run --project src/MiniDb -- --db example.db --file scripts/example.sql
```

Ошибки SQL-файла выводятся в stderr; последующие команды выполняются, код завершения будет 1, если была ошибка. UTF-8 BOM поддерживается. SQL-комментарии не поддерживаются.

## Поддерживаемый SQL

Ключевые слова и имена таблиц/столбцов нечувствительны к регистру. Строки сравниваются чувствительно к регистру, порядком Unicode (ordinal). INT — знаковое целое 64 бит. Одинарная кавычка в строке удваивается.

```sql
CREATE TABLE people (id INT PRIMARY KEY, name TEXT, age INT);
INSERT INTO people VALUES (1, 'Анна', 25), (2, 'Иван', 31);
INSERT INTO people (age, id, name) VALUES (40, 3, 'O''Brien');
SELECT * FROM people;
SELECT name, age FROM people WHERE age >= 25 AND (id != 2 OR name = 'Иван') ORDER BY age DESC LIMIT 10;
SELECT * FROM people WHERE age <> 40;
SELECT * FROM people WHERE age < 31 AND id <= 2;
UPDATE people SET age = 26, name = 'Анна Петрова' WHERE id = 1;
DELETE FROM people WHERE age > 35;
SELECT COUNT(*), SUM(age), AVG(age) FROM people;
SELECT age, COUNT(*), SUM(id), AVG(id) FROM people GROUP BY age ORDER BY age ASC;
CREATE INDEX idx_people_age ON people(age);
EXPLAIN SELECT * FROM people WHERE age = 26 AND id > 0;
BEGIN;
UPDATE people SET age = 99 WHERE id = 1;
SELECT * FROM people WHERE age = 99;
ROLLBACK;
BEGIN;
INSERT INTO people VALUES (4, 'Олег', 20);
COMMIT;
CREATE TABLE orders (id INT PRIMARY KEY, person_id INT, amount INT);
INSERT INTO orders VALUES (10, 1, 100), (11, 2, 200);
SELECT people.name, orders.amount FROM people JOIN orders ON people.id = orders.person_id ORDER BY orders.amount DESC;
SELECT people.name, SUM(orders.amount) FROM people JOIN orders ON people.id = orders.person_id GROUP BY people.name ORDER BY people.name;
```

`AND` имеет более высокий приоритет, чем `OR`; скобки меняют порядок. INSERT с перечнем столбцов требует все столбцы ровно по одному, так как NULL и DEFAULT отсутствуют. UPDATE принимает литералы, не арифметические выражения. SELECT проверяет неизвестные и неоднозначные имена даже для пустых таблиц. Одна таблица может иметь один PRIMARY KEY; дубликаты в обычных индексах допустимы. COUNT(column) совпадает с COUNT(*), так как NULL отсутствует. SUM/AVG работают по INT; результат — decimal, пустой SUM — 0, пустой AVG — текст NULL. GROUP BY допускает несколько столбцов; неагрегатные поля должны входить в GROUP BY.

## Тесты

```powershell
dotnet build -c Release
dotnet test -c Release --no-build
```

Тесты проверяют лексер/AST/позиции ошибок, приоритеты, SQL, ограничения, атомарность, страницы, индексы, JOIN, COMMIT/ROLLBACK. Пять интеграционных аварийных тестов запускают реальный отдельный процесс и вызывают `Kill(true)`: внутри транзакции, посередине тела WAL, перед commit marker, после его fsync и во время checkpoint. Затем проверяются данные и индекс, оборванный хвост и повторное восстановление. Исключения вместо убийства процесса не используются. CI настроен для Windows и Linux; удалённый GitHub Actions необходимо запускать после отправки репозитория на GitHub.

## 100 000 строк и измерение

```powershell
dotnet run -c Release --project src/MiniDb -- --db large.db --generate 100000
```

Генератор создаёт users с id и age от 1 до 100000, именами «Пользователь N». База должна быть новой. Далее в интерактивном режиме или SQL-файле:

```sql
EXPLAIN SELECT * FROM users WHERE age = 54321;
SELECT * FROM users WHERE age = 54321;
CREATE INDEX idx_age ON users(age);
EXPLAIN SELECT * FROM users WHERE age = 54321;
SELECT * FROM users WHERE age = 54321;
```

Быстрый локальный замер (индекс уже должен существовать):

```powershell
dotnet run -c Release --project src/MiniDb -- --db large.db --measure
```

Полноценный BenchmarkDotNet-прогон создаёт собственные 100000 строк в памяти, строит индекс в GlobalSetup и проверяет совпадение результата. Поиск выполняется через один и тот же исполнитель и AST: переключается только разрешение использовать индекс. Генерация, построение дерева, разбор SQL и диск не входят в замер; это сравнение поиска в открытой базе, а не холодного чтения диска.

```powershell
dotnet run -c Release --project benchmarks/MiniDb.Benchmarks
```

Отчёты появятся в `BenchmarkDotNet.Artifacts/` (не коммитятся). Фактический результат облачного запуска: **1842.930 мкс полный перебор, 2.456 мкс индекс**, отношение средних **около 750×**. Условие 100× достигнуто в этом запуске; результат зависит от компьютера и селективности. Подробности и погрешность: [docs/benchmark.md](docs/benchmark.md).

## Сценарий защиты и аварийное восстановление

Автоматический сценарий Windows (PowerShell, из корня):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/defense.ps1
```

Для Linux/macOS с .NET 8: `bash scripts/defense.sh`.

Сценарий создаёт новую временную базу, загружает 100000 строк, показывает Full scan, строит индекс, показывает Index seek, проверяет равенство результатов и измеряет время. Затем отдельный процесс выполняет BEGIN, UPDATE id=1 и INSERT id=100001, сигнализирует о готовности и останавливается до COMMIT. Родитель принудительно завершает его и открывает базу заново. id=1 остаётся с age=1, id=100001 отсутствует, age=99 принадлежит только исходной строке id=99. Временная папка печатается в конце и сохраняется для просмотра.

Для демонстрации именно redo подтверждённого WAL вместо `uncommitted` в сценарии замените точку на `after-commit` или `checkpoint`. После убийства и открытия появится сообщение WAL о восстановлении; id=1 получит age=99, id=100001 появится. Точка `before-commit` оставит полный неподтверждённый frame в WAL, который будет отброшен при открытии. Эти режимы предназначены только для тестов и демонстрации, процесс специально ждёт принудительного завершения. Сами тесты уже покрывают все пять точек. Команды для ручного запуска дочернего процесса:

```powershell
dotnet src/MiniDb/bin/Release/net8.0/MiniDb.dll --db large.db --crash-worker --point after-commit --signal ready.txt
```

Дождитесь появления ready.txt, завершите именно этот процесс через диспетчер задач/Stop-Process, затем запустите обычное приложение с той же базой. Перед новым опытом удалите старый ready.txt. Не держите базу одновременно открытой в другом терминале.

## Ограничения

- Учебная single-user база: один процесс на файл, без конкурентных запросов и потокобезопасности.
- Все строки и B-деревья загружаются в RAM. Стабильный rowId отображается на массив значений; физические страницы уплотняются при checkpoint. Это не дисковый buffer pool.
- Размер страницы 4096, максимальная бинарная запись 4092 байта с учётом служебных полей, UTF-8 и имени таблицы; максимум 256 столбцов, схема тоже должна помещаться. Длинные строки отклоняются. Overflow-страниц нет.
- Изменения копируют состояние для отката; индексы перестраиваются после команды. Каждое подтверждение пишет полный страничный образ в WAL и основной файл. Это надёжно для учебного объёма, но медленно для множества одиночных INSERT. Используйте один многострочный INSERT/генератор.
- Индекс ускоряет `column = literal` (в том числе внутри AND) для SELECT одной таблицы. Для диапазонов, OR, JOIN — полный перебор. ORDER BY не использует индекс. PRIMARY KEY проверяется HashSet, не создаёт индекс автоматически.
- JOIN ровно двух разных таблиц, INNER nested loop, ON — равенство столбцов разных таблиц. Alias, self join, outer join, подзапросы и выражения проекции отсутствуют.
- ORDER BY — один столбец; для агрегатного запроса он должен быть в результате. ASC по умолчанию. Агрегаты без alias; HAVING отсутствует.
- NULL, DEFAULT, FOREIGN KEY, составной PRIMARY KEY, ALTER/DROP, SQL-комментарии и несколько SQL-команд в одном вызове API Execute отсутствуют (CLI SQL-файлы разбивает сам).
- WAL защищает от аварии процесса/оборванного хвоста. Гарантии питания зависят от fsync файловой системы. Защита от произвольной порчи носителя отсутствует. После ошибки I/O требуется закрыть и заново открыть базу.

Дополнительно: [план](docs/plan.md), [прогресс](docs/progress.md), [архитектура](docs/architecture.md), [протокол WAL](docs/wal.md), [подготовка к защите](docs/defense.md).
