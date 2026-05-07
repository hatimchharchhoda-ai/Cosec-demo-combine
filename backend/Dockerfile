# ---------- FRONTEND BUILD ----------
FROM node:20 AS frontend-build
WORKDIR /app
COPY frontend/package*.json ./
RUN npm install
COPY frontend ./
RUN npm run build -- --configuration production

# ---------- BACKEND BUILD ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend ./backend
WORKDIR /src/backend
RUN dotnet restore "COSEC-demo.csproj"
RUN dotnet publish "COSEC-demo.csproj" -c Release -o /app/publish

# Copy Angular dist into wwwroot
COPY --from=frontend-build /app/dist/frontend/browser /app/publish/wwwroot

# ---------- RUNTIME ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "COSEC-demo.dll"]