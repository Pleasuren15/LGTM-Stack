# Use the official .NET 9 runtime as a base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the official .NET 9 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["LGTM.Stack/LGTM.Stack.csproj", "LGTM.Stack/"]
RUN dotnet restore "LGTM.Stack/LGTM.Stack.csproj"
COPY . .
WORKDIR "/src/LGTM.Stack"
RUN dotnet build "LGTM.Stack.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "LGTM.Stack.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create a non-root user
RUN addgroup --system --gid 1001 appgroup \
    && adduser --system --uid 1001 --ingroup appgroup --shell /bin/false appuser

# Create necessary directories and set permissions
RUN mkdir -p /tmp && chown -R appuser:appgroup /app /tmp

# Switch to non-root user
USER appuser

ENTRYPOINT ["dotnet", "LGTM.Stack.dll"]