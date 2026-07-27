# 📚 PrintingBooks Management Portal

A modern, professional book printing management system built with ASP.NET Core Blazor Server and Entity Framework Core.

## 🚀 Features

### 🎨 Modern UI/UX
- **Professional Sidebar Navigation** with active state highlighting
- **Responsive Design** that works on desktop and mobile
- **Modern Blue Gradient Theme** with subtle pattern overlays
- **Smooth Animations** and hover effects
- **Accessibility Support** (keyboard navigation, screen readers, high contrast)

### 👥 Role-Based Access Control
- **Admin Dashboard** with analytics and management tools
- **Shop Interface** for book viewing and printing
- **Secure Authentication** with ASP.NET Core Identity

### 📊 Management Features
- **Educational Boards Management**
- **Books Catalog** with file upload and organization
- **Shop Management** with book assignments
- **Print History Tracking**
- **Analytics Dashboard** with real-time statistics

## 🛠️ Tech Stack

- **Backend:** ASP.NET Core 10.0 (Blazor Server)
- **Database:** Entity Framework Core with SQL Server
- **Authentication:** ASP.NET Core Identity
- **UI Framework:** Bootstrap 5 + Custom CSS
- **Real-time:** SignalR for live updates
- **File Handling:** IFormFile with secure PDF processing

## 🎯 Modern Sidebar Navigation

The sidebar features a completely modern design following professional UI standards:

### ✨ Visual Features
- **Active State Highlighting:** Left border + background + indicator dot
- **Smooth Hover Effects:** Icon scaling and slide animations
- **Professional Gradient Background:** Blue theme (1e3a8a → 1d4ed8)
- **Enhanced Typography:** Modern font weights and spacing
- **Subtle Pattern Overlay:** Adds texture without distraction

### 🔧 Technical Implementation
- **Custom Navigation Logic:** Real-time route tracking
- **Accessibility Compliant:** WCAG guidelines followed
- **Performance Optimized:** Efficient CSS and animations
- **Mobile Responsive:** Adapts to all screen sizes

## 🚀 Quick Start

### Prerequisites
- .NET 10.0 SDK
- SQL Server (LocalDB or full instance)
- Visual Studio 2022 or VS Code

### Installation
1. Clone the repository:
   ```bash
   git clone https://github.com/mustfamaheer-dotcom/BookPrintingPortal.git
   cd BookPrintingPortal
   ```

2. Update database connection in `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=PrintingBooksPortal;Trusted_Connection=true;MultipleActiveResultSets=true"
     }
   }
   ```

3. Run database migrations:
   ```bash
   dotnet ef database update
   ```

4. Start the application:
   ```bash
   dotnet run
   ```

5. Navigate to `https://localhost:5001`

## 📦 Deployment

### WebDeploy to RunASP.NET

The project is configured for deployment to RunASP.NET hosting:

**Server Details:**
- **URL:** http://drbaheegbook.runasp.net/
- **Server:** site79455.siteasp.net:8172
- **Site:** site79455

**Deploy using Visual Studio:**
1. Right-click project → Publish
2. Select the WebDeploy profile
3. Enter credentials when prompted
4. Click Publish

**Deploy using CLI:**
```bash
dotnet publish -c Release -p:PublishProfile=WebDeploy
```

### Environment Configuration

Update `appsettings.Production.json` for production:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Your-Production-Database-Connection-String"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

## 📁 Project Structure

```
📦 PrintingBooksPortal/
├── 📂 Components/
│   ├── 📂 Layout/
│   │   ├── 🎨 NavMenu.razor          # Modern sidebar navigation
│   │   ├── 🎨 NavMenu.razor.css      # Professional styling
│   │   ├── 🖥️ MainLayout.razor       # Main app layout
│   │   └── 🖥️ MainLayout.razor.css   # Layout styling
│   ├── 📂 Pages/
│   │   ├── 📂 Admin/                 # Admin-only pages
│   │   ├── 📂 Public/                # Public access pages
│   │   └── 📂 Shop/                  # Shop user pages
│   └── 🔗 Routes.razor               # App routing
├── 📂 Controllers/                   # API controllers
├── 📂 Data/                         # Entity Framework
├── 📂 Models/                       # Data models
├── 📂 Migrations/                   # EF migrations
└── 📂 wwwroot/                      # Static files
```

## 🎨 UI Components

### Sidebar Navigation
- **Modern gradient background** with subtle patterns
- **Active state highlighting** with left border and glow
- **Smooth animations** for professional feel
- **Responsive design** for mobile compatibility

### Dashboard Cards
- **Stat cards** with gradient backgrounds
- **Interactive elements** with hover effects
- **Real-time data updates**
- **Modern typography** and spacing

### Data Tables
- **Modern table styling** with hover effects
- **Action buttons** with consistent design
- **Status badges** with color coding
- **Responsive layout** for mobile viewing

## 🔒 Security Features

- **Role-based authorization** (Admin/Shop)
- **Secure file upload** with validation
- **SQL injection protection** via EF Core
- **XSS protection** with Blazor's built-in security
- **HTTPS enforcement** in production

## 📊 Analytics & Reporting

- **Real-time dashboard** with key metrics
- **Print activity tracking**
- **Shop performance analytics**
- **Book usage statistics**

## 🚦 API Endpoints

### Authentication
- `POST /login` - User login
- `POST /logout` - User logout

### Admin APIs
- `GET /api/analytics` - Dashboard statistics
- `POST /api/books` - Upload new books
- `GET /api/shops` - Manage shops

### File Management
- `POST /api/files/upload` - Secure file upload
- `GET /api/files/{id}` - Download files

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## 📄 License

This project is proprietary software developed for educational book printing management.

## 📞 Support

For technical support or questions:
- **Repository:** [GitHub Issues](https://github.com/mustfamaheer-dotcom/BookPrintingPortal/issues)
- **Email:** Contact the development team

---

## 🎯 Recent Updates

### Modern Sidebar Navigation (Latest)
- ✅ **Complete UI overhaul** with professional design
- ✅ **Real-time active state detection**
- ✅ **Smooth animations and transitions**
- ✅ **Accessibility compliance** (WCAG 2.1)
- ✅ **Mobile responsive design**
- ✅ **Performance optimizations**

The sidebar now provides a truly modern, professional experience that matches contemporary admin dashboard standards.

---

*Built with ❤️ using ASP.NET Core Blazor*