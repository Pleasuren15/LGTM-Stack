# Use the official .NET 9 runtime as a base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

RUN apt-get update && apt-get install -y ca-certificates && rm -rf /var/lib/apt/lists/*
COPY ["nscacert.pem", "/usr/local/share/ca-certificates/netskope.crt"]
RUN update-ca-certificates

# Use the official .NET 9 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Install certificate in build stage
# Install certificate in build stage
# Install certificate in build stage

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