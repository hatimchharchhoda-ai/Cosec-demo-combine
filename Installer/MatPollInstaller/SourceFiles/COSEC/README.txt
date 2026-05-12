After installation, open:
C:\Program Files\COSEC\appsettings.json

Fill in your SQL Server details:
- Database: your database name
- User Id: your SQL Server login username  
- Password: your SQL Server login password

Make sure SQL Server Authentication is ENABLED in SSMS:
  Right-click server → Properties → Security → 
  Select "SQL Server and Windows Authentication mode"

Then restart the COSEC service:
  Services → COSECService → Restart
  (or run: net stop COSECService && net start COSECService)