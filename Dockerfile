FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY src/PayBot/PayBot.csproj src/PayBot/
RUN RID=linux-x64; if [ "$TARGETARCH" = "arm64" ]; then RID=linux-arm64; fi; \
    dotnet restore src/PayBot/PayBot.csproj -r "$RID"
COPY src/PayBot/ src/PayBot/
RUN RID=linux-x64; if [ "$TARGETARCH" = "arm64" ]; then RID=linux-arm64; fi; \
    dotnet publish src/PayBot/PayBot.csproj -c "$BUILD_CONFIGURATION" -r "$RID" --self-contained false --no-restore -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "PayBot.dll"]
