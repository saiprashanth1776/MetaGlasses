# 🕶️ MetaGlasses - MR Utilities for Meta Quest 3  
*Blending the real and digital worlds — one utility at a time.*

A Mixed Reality (MR) prototype for the **Meta Quest 3**, designed to bring together three powerful, real-world inspired utilities — **Maps & Navigation**, **Live Translation**, and **Camera Capture** — all integrated into an immersive MR environment.  

The goal? To recreate the *“Meta Glasses”* experience right inside the Quest — where your headset becomes your smart assistant for understanding, navigating, and capturing the world around you.  

---

## 🗺️ 1. Maps & Navigation  
A live, dynamic map rendered right inside your MR space.  

- Uses **Google Maps Tiles API** to stream real map data into Unity.  
- Centers dynamically based on the **latitude/longitude** input by the user.  
- Styled to match Google Maps’ *direction view* — clean roads, landmarks, parks, and subtle highlights.  
- Includes a **directional arrow** that rotates smoothly based on your **headset’s orientation**, helping you stay aligned with real-world north.  

> 🧭 Note: The Quest 3 doesn’t have GPS, so the map centers based on user input. You’ll need a Google API key with Maps Tiles API access.  

---

## 🗣️ 2. Live Translation  
Your headset becomes your interpreter.  

- Tap your **right thumb** to start listening.  
- Speech is captured via **Meta’s Wit.ai**, converted to text, and then translated via the **Google Translate API**.  
- Translated text is shown right in your MR space inside a minimal dialog box.  
- You can customize the **source and target languages** directly in the Unity scene (`LiveTranslation`).  

> 🧩 Setup required:  
> - Create a **Wit.ai app**, copy its credentials into the Unity scene.  
> - Add your **Google Translate API key**.  

---

## 📸 3. Camera Capture  
Snap pictures through your Quest — just like AR glasses.  

- Uses **Meta’s Passthrough Camera API** to simulate real-world photo capture.  
- **Thumb swipe forward/backward** to zoom in or out.  
- **Thumb tap** to capture a photo.  

This feature blends the passthrough view with interaction-based control, letting you “take photos” of your surroundings in a seamless MR flow.

---

## 🧠 Tech Stack
- **Unity (URP)**  
- **Meta SDKs** (Passthrough, Hand Tracking)  
- **Google Maps Tiles API**  
- **Wit.ai Speech Recognition**  
- **Google Translate API**

---

## ⚙️ Setup Instructions
1. Clone this repo:  
   ```bash
   git clone https://github.com/saiprashanth1776/MetaGlasses.git
   ```
2. Add your API keys:  
   - Google Maps Tiles API key  
   - Google Translate API key  
   - Wit.ai App credentials  
3. Open the project in Unity (tested with Unity 2022.3+).  
4. Load the relevant scenes:  
   - `MapsScene`  
   - `LiveTranslation`  
   - `CameraCapture`  
5. Build & Run on **Meta Quest 3**.   
