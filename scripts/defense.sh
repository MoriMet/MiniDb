#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build -c Release
work=$(mktemp -d)
app=src/MiniDb/bin/Release/net8.0/MiniDb.dll
dotnet "$app" --db "$work/demo.db" --generate 100000
cat > "$work/queries.sql" <<'SQL'
EXPLAIN SELECT * FROM users WHERE age=54321;
SELECT * FROM users WHERE age=54321;
CREATE INDEX idx_age ON users(age);
EXPLAIN SELECT * FROM users WHERE age=54321;
SELECT * FROM users WHERE age=54321;
SQL
dotnet "$app" --db "$work/demo.db" --file "$work/queries.sql"
dotnet "$app" --db "$work/demo.db" --measure
dotnet "$app" --db "$work/demo.db" --crash-worker --point uncommitted --signal "$work/ready" &
worker=$!
trap 'kill -9 "$worker" 2>/dev/null || true' EXIT
for ((i=0;i<600;i++)); do [[ -f "$work/ready" ]] && break; kill -0 "$worker"; sleep .05; done
[[ -f "$work/ready" ]]
kill -9 "$worker"
wait "$worker" || true
trap - EXIT
printf '%s\n' 'SELECT * FROM users WHERE id=1; SELECT * FROM users WHERE id=100001; SELECT * FROM users WHERE age=99;' > "$work/queries.sql"
dotnet "$app" --db "$work/demo.db" --file "$work/queries.sql"
echo "Демонстрация завершена. Файлы: $work"
