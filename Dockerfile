# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["CardGame.Server.csproj", "./"]
RUN dotnet restore "CardGame.Server.csproj"

COPY . .
RUN dotnet publish "CardGame.Server.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Expose port (Render and container clouds use PORT env variable)
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "CardGame.Server.dll"]
