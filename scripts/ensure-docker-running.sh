#!/bin/bash

# Ensure Docker Desktop is running on macOS and bendb-sql container is started

set -e

echo "🐳 Checking Docker status..."

# Check if Docker Desktop is running on macOS
if ! docker info > /dev/null 2>&1; then
    echo "❌ Docker is not running. Starting Docker Desktop..."

    # Try to start Docker Desktop on macOS
    open /Applications/Docker.app

    # Wait for Docker to be ready
    echo "⏳ Waiting for Docker to start (up to 30 seconds)..."
    for i in {1..30}; do
        if docker info > /dev/null 2>&1; then
            echo "✅ Docker is ready."
            break
        fi
        echo -n "."
        sleep 1
    done

    if ! docker info > /dev/null 2>&1; then
        echo "❌ Docker failed to start. Please start Docker Desktop manually."
        exit 1
    fi
else
    echo "✅ Docker is running."
fi

# Check if bendb-sql container exists and is running
echo "🔍 Checking bendb-sql container..."

CONTAINER_STATUS=$(docker inspect --format='{{.State.Status}}' bendb-sql 2>/dev/null || echo "missing")

if [ "$CONTAINER_STATUS" = "running" ]; then
    echo "✅ bendb-sql is already running."
elif [ "$CONTAINER_STATUS" = "exited" ]; then
    echo "⏳ Starting bendb-sql container..."
    docker start bendb-sql
    echo "✅ bendb-sql started."

    # Wait for SQL Server to be ready
    echo "⏳ Waiting for SQL Server to be ready (up to 30 seconds)..."
    for i in {1..30}; do
        if docker exec bendb-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Password1' -Q "SELECT 1" > /dev/null 2>&1; then
            echo "✅ SQL Server is ready."
            break
        fi
        echo -n "."
        sleep 1
    done
else
    echo "❌ bendb-sql container not found. Please create it with:"
    echo ""
    echo "docker run \\"
    echo "  -e 'ACCEPT_EULA=Y' \\"
    echo "  -e 'SA_PASSWORD=YourStrong@Password1' \\"
    echo "  -p 1433:1433 \\"
    echo "  --name bendb-sql \\"
    echo "  --restart unless-stopped \\"
    echo "  -d mcr.microsoft.com/mssql/server:2022-latest"
    echo ""
    exit 1
fi

echo "✅ Docker and SQL Server are ready. You can now run the WebApi."
