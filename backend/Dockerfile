# ---------- STAGE 1 : Build Angular ----------
FROM node:20 AS ngbuild
WORKDIR /ng
COPY ../frontend/package*.json ./
RUN npm install
COPY ../frontend .
RUN npm run build

# ---------- STAGE 2 : Build .NET API ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

# ---------- STAGE 3 : Final runtime with SQL Server ----------
FROM mcr.microsoft.com/mssql/server:2022-latest

ENV ACCEPT_EULA=Y
ENV SA_PASSWORD=Your_password123
ENV MSSQL_PID=Express

# install dotnet runtime + node deps for serving files
RUN apt-get update && \
    apt-get install -y wget apt-transport-https && \
    wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y aspnetcore-runtime-8.0

WORKDIR /app

# copy published api
COPY --from=build /app/publish .

# copy angular build into wwwroot
COPY --from=ngbuild /ng/dist /app/wwwroot

EXPOSE 8080 1433

CMD /bin/bash -c "\
/opt/mssql/bin/sqlservr & \
sleep 25 && \
dotnet COSEC-demo.dll --urls=http://0.0.0.0:8080"