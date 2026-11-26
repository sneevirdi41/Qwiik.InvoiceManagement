FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Qwiik.InvoiceManagement.slnx ./
COPY Qwiik.InvoiceManagement.Domain/Qwiik.InvoiceManagement.Domain.csproj Qwiik.InvoiceManagement.Domain/
COPY Qwiik.InvoiceManagement.Application/Qwiik.InvoiceManagement.Application.csproj Qwiik.InvoiceManagement.Application/
COPY Qwiik.InvoiceManagement.Infrastructure/Qwiik.InvoiceManagement.Infrastructure.csproj Qwiik.InvoiceManagement.Infrastructure/
COPY Qwiik.InvoiceManagement.API/Qwiik.InvoiceManagement.API.csproj Qwiik.InvoiceManagement.API/
COPY Qwiik.InvoiceManagement.Tests/Qwiik.InvoiceManagement.Tests.csproj Qwiik.InvoiceManagement.Tests/

RUN dotnet restore Qwiik.InvoiceManagement.API/Qwiik.InvoiceManagement.API.csproj

COPY . .
RUN dotnet publish Qwiik.InvoiceManagement.API/Qwiik.InvoiceManagement.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Qwiik.InvoiceManagement.API.dll"]
