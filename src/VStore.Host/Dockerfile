FROM docker-hub.2gis.ru/dotnet/core/runtime-deps:2.2.5-alpine3.9
WORKDIR /app
EXPOSE 5000
ENV ASPNETCORE_URLS http://*:5000

ENTRYPOINT ["./VStore.Host"]

COPY . .