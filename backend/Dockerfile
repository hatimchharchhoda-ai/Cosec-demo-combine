# ---------- STAGE 1 : Build Angular ----------
FROM node:20 AS ngbuild
WORKDIR /frontend
COPY ../frontend/package*.json ./
RUN npm install
COPY ../frontend .
RUN npm run build

# ---------- STAGE 2 : Build .NET ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish COSEC-demo.csproj -c Release -o /app/publish

# ---------- STAGE 3 : Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .
COPY --from=ngbuild /frontend/dist /app/wwwroot

EXPOSE 8080
CMD ["dotnet", "COSEC-demo.dll", "--urls=http://0.0.0.0:8080"]