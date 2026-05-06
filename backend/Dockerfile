# ---------- STAGE 1 : Build Angular ----------
FROM node:20 AS ngbuild
WORKDIR /ng
COPY ../frontend/package*.json ./
RUN npm install
COPY ../frontend .
RUN npm run build

# ---------- STAGE 2 : Build .NET ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ---------- STAGE 3 : Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install SQL Server on this image
RUN apt-get update && \
    apt-get install -y curl apt-transport-https gnupg && \
    curl https://packages.microsoft.com/keys/microsoft.asc | apt-key add - && \
    curl https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2022.list > /etc/apt/sources.list.d/mssql-server.list && \
    apt-get update && \
    ACCEPT_EULA=Y apt-get install -y mssql-server

ENV SA_PASSWORD=matrix_1
ENV ACCEPT_EULA=Y
ENV MSSQL_PID=Express

WORKDIR /app

# copy api
COPY --from=build /app/publish .

# copy angular into wwwroot
COPY --from=ngbuild /ng/dist /app/wwwroot

EXPOSE 8080 1433

CMD /bin/bash -c "\
/opt/mssql/bin/sqlservr & \
sleep 25 && \
dotnet COSEC-demo.dll --urls=http://0.0.0.0:8080"