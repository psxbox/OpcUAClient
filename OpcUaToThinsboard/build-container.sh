#!/bin/bash

# Publish the .NET project to a container image
dotnet publish \
    --os linux \
    --arch x64 \
    -c Release \
    -p:PublishProfile=DefaultContainer \
    -p:ContainerRepository=opcua-to-thingsboard

# Example: To push to Docker Hub, set ContainerRegistry to your Docker Hub username
# -p:ContainerRegistry=mydockerhubusername