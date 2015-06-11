# BabyMonitor
Baby Monitor Application Leveraging Kinect SDK v2

This project exists as an exploration of adulthood and the Kinect SDK.

The goal is to see how much information can be gleaned from a single static Kinect.

At a bare minimum this should include Color and Infrared Video feeds as well as audio.

But it could potentially attempt heart rate and respirartory rate monitoring, and an estimation of temperature.

Likely less reliably, but possibly entertaining would be leveraging the face source to detect whether the subject
is awake or asleep, as well as their mood.

## Getting Started
The Kinect SDK v2 will need to be installed.  A Kinect will need to be connected (see relevant documentation).  I'd recommend running Kinect Studio v2 to test out your connection, USB Controller, etc.

The Face tracking components require the folder NuiDatabase - the easiest way is copying it from the Debug folder of a face-related sample project, into the Debug/Release folder of your project.  One possible location:

C:\Program Files\Microsoft SDKs\Kinect\v2.0_1409\Samples\Managed\FaceBasics-WPF\bin\x64\Debug\NuiDatabase

## Status: Early Development
Code will (mostly) do what it says it does, but many features remain unimplemented
