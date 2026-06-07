# 🚗 Iranian-mYPMS-client 



> ASP.NET Core client for the **mYPMS Parking Management System** with integrated Iranian License Plate Recognition (ALPR) support through a FastAPI-based recognition service.



---



# 📋 Table of Contents



* Overview

* Features

* Project Structure

* Prerequisites

* Installation & Setup

* Configuration

* System Architecture

* API Endpoints

* ALPR Workflow

* License



---



# 📌 Overview



**Iranian-mYPMS-client** is the server-side component of the **mYPMS Parking Management System**. 



The application is responsible for:



* Vehicle entry and exit registration

* Communication with IP cameras

* Integration with the Iranian ALPR service

* Parking fee calculation

* Gate and parking lot management

* Traffic record storage

* Windows Service deployment



The system communicates with a dedicated FastAPI-based ALPR service to recognize Iranian license plates from captured vehicle images.



---



# ✨ Features



* 🚗 Vehicle entry and exit registration

* 📷 IP camera snapshot capture

* 🇮🇷 Iranian license plate recognition

* 🔗 FastAPI ALPR integration

* 💳 RFID card support

* 💰 Automatic parking fee calculation

* 🗄 SQL Server database integration

* ⚙️ ASP.NET Core dependency injection

* 🪟 Windows Service deployment

* 📊 Session-based parking and gate management



---



# 📁 Project Structure



```text

Iranian-mypms-client/
├── Program.cs              # Application entry point
├── HomeController.cs       # Main traffic and parking operations
├── SATPA.cs                # ALPR FastAPI client service
├── AlprOptions.cs          # ALPR configuration model
├── AlprResult.cs           # ALPR response model
├── appsettings.json        # Application configuration
└── .gitignore

```



---



# 🔧 Prerequisites

| Tool                 | Recommended Version |
|---------------------|---------------------|
| .NET SDK            | 8.0+                |
| SQL Server          | 2019+               |
| FastAPI ALPR Service| Running and reachable |
| IP Camera           | Snapshot-capable camera |
| Windows Server      | Recommended for production |


---



# 🚀 Installation & Setup



## 1. Clone the Repository



```bash

git clone https://github.com/MaryamJangjoo/Iranian-mYPMS-client.git 

cd Iranian-mYPMS-client 

```



## 2. Configure Application Settings



Edit:



```text

appsettings.json

```



according to your environment.



## 3. Build and Run



```bash

dotnet build

dotnet run

```



---



## Windows Service Deployment



Publish the application:



```bash

dotnet publish -c Release -o ./publish

```



Create the service:



```bash

sc create mYPMS binPath="C:\path\to\publish\mYPMS.exe"

```



Start the service:



```bash

sc start mYPMS

```



---



# ⚙️ Configuration



Example:



```json

{

  "ConnectionStrings": {

    "CNS": "Server=.;Database=mYPMS;Trusted_Connection=True;"

  },

  "Alpr": {

    "BaseUrl": "http://localhost:8000",

    "MinConfidence": 0.4,

    "TimeoutSeconds": 10

  }

}

```



## ALPR Settings



| Key            | Description                       |

| -------------- | --------------------------------- |

| BaseUrl        | ALPR FastAPI service URL          |

| MinConfidence  | Minimum accepted confidence score |

| TimeoutSeconds | Request timeout                   |



---



# 🏗 System Architecture



```text

IP Camera
   │ Snapshot
   ▼
Iranian-mypms-client (ASP.NET Core)
   │
   │ POST /recognize
   ▼
FastAPI ALPR Service
   │
   ▼
License Plate + Confidence
   │
   ▼
SQL Server

```



---



# 🔄 Traffic Registration Workflow



1. Operator selects parking lot and gate.

2. RFID card is scanned.

3. Vehicle image is captured from the camera.

4. Image is sent to the ALPR service.

5. License plate is recognized.

6. Traffic information is stored in SQL Server.

7. Parking fee is calculated.

8. Vehicle entry or exit is completed.



---



# 📡 API Endpoints



| Route                     | Method | Description                 |

| ------------------------- | ------ | --------------------------- |

| /Home/Welcome             | GET    | Select parking and gate     |

| /Home/Index               | GET    | Main dashboard              |

| /Home/Traffic             | POST   | Register traffic            |

| /Home/GetTraffic          | GET    | Preview traffic             |

| /Home/DeleteTraffic       | GET    | Delete traffic record       |

| /Home/GetParkingWithGates | GET    | List parking lots and gates |

| /Home/GetInquiry          | GET    | Card inquiry                |

| /Home/Licence             | GET    | Version and ALPR status     |



---



# 🤖 ALPR Workflow



The application communicates with the FastAPI ALPR service through the `SATPA.cs` client.



### Main Methods



#### RecognizeAsync(byte[])



Uploads an image and returns:



* Plate number

* Confidence score

* Validation status



#### IsAliveAsync()



Checks ALPR service availability.



### Validation Rules



* Confidence must be above `MinConfidence`

* Plate must contain exactly 8 valid characters

* Persian digits are normalized to ASCII digits

* Tokens such as `ایران`, `IRAN`, and `IR` are removed



---



# 📂 Image Storage



```text

wwwroot/

├── ParkingImages/

│   └── YYYY-MM-DD/

│       ├── en-{trafficId}.png

│       └── ex-{trafficId}.png

│

└── temp/

```



Temporary images are automatically cleaned after several hours.



---



# 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

