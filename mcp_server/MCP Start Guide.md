# MCP Server Guide 🚀

This guide explains how to set up and use the MCP (Middleware and Computation Platform) server, which acts as a bridge between your Grasshopper plugin and external tools like Cloud Code.

## 1. How it Works

The data flow is simple and robust:

1.  **Grasshopper Plugin**: Your C# component runs a WebSocket server on `ws://localhost:8181/live`. This is the source of truth for the canvas data.
2.  **MCP Server (This App)**: A Node.js application that acts as a middleman.
    * It runs an HTTP server on `http://localhost:3000`.
    * When it receives an HTTP request (e.g., to `/grasshopper/canvas`), it opens a WebSocket connection to your Grasshopper plugin, requests the data, and waits for the response.
    * It can perform additional tasks, like calling an AI model to analyze the canvas.
3.  **Cloud Code / External Tool**: Your end application makes simple HTTP requests to the MCP server, without needing to know anything about the underlying WebSocket communication.

This architecture decouples your tool from Grasshopper, making the system more modular and easier to maintain.

## 2. Standalone Setup & Debugging

Before automating the server launch, you need to be able to run and test it independently. This is crucial for development.

**Prerequisites:**

* [Node.js](https://nodejs.org/) installed on your machine.

**Steps:**

1.  **Save the Files**: Create a folder named `mcp_server` and save the `package.json` and `server.js` files inside it.
2.  **Install Dependencies**: Open a terminal, navigate into the `mcp_server` folder, and run:
    ```
    npm install
    ```
    This will download the necessary libraries (`express`, `cors`, `ws`) into a `node_modules` folder.
3.  **Run the Server**: Start the server with:
    ```
    npm start
    ```
    You should see a confirmation message in the terminal:
    ```
    [MCP] Server started successfully.
    [MCP] Listening for HTTP requests on http://localhost:3000
    ```
4.  **Test the Endpoints**:
    * Make sure Rhino and Grasshopper are running with your `LiveCodingGH.gha` component placed on the canvas.
    * **Status Check**: Open a web browser and go to `http://localhost:3000/status`. You should see a JSON response confirming it's running.
    * **Get Canvas Data**: In a new terminal, use `curl` to test the main endpoint:
        ```
        curl http://localhost:3000/grasshopper/canvas
        ```
        You should get a JSON response containing the full definition of your active Grasshopper canvas.
    * **Get AI Overview**: Use `curl` to test the AI summary endpoint:
        ```
        curl -X POST http://localhost:3000/grasshopper/overview
        ```
        This will return the mock AI-generated markdown summary.

## 3. Integrating with the Grasshopper Plugin

Here's the magic part: launching the MCP server automatically from your C# component.

TO BE CONTINUED...