# Render אינו מריץ .NET ישירות, ולכן השרת נפרס כתמונת Docker.
# בנייה בשני שלבים: תמונת ה-SDK בונה, ותמונת ה-runtime הקטנה מריצה

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# קודם מעתיקים רק את קובצי ה-csproj ומשחזרים חבילות, כדי ש-Docker ישתמש ב-cache כשהקוד משתנה והחבילות לא
COPY ParkingSystem.Core/ParkingSystem.Core.csproj ParkingSystem.Core/
COPY ParkingSystem.Data/ParkingSystem.Data.csproj ParkingSystem.Data/
COPY ParkingSystem.Service/ParkingSystem.Service.csproj ParkingSystem.Service/
COPY ParkingSystem.API/ParkingSystem.API.csproj ParkingSystem.API/
RUN dotnet restore ParkingSystem.API/ParkingSystem.API.csproj

COPY . .
RUN dotnet publish ParkingSystem.API/ParkingSystem.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# תיקיית הלוגים של NLog (${basedir}/logs). השרת רץ כמשתמש רגיל ולא כ-root, ולכן הוא חייב הרשאת כתיבה
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production

# הפורט נקבע בזמן ריצה ממשתנה PORT של Render (Program.cs). ברירת המחדל של Render היא 10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "ParkingSystem.API.dll"]
