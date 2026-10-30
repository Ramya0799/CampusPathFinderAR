import "dotenv/config";
import express from "express";
import { createExpressMiddleware } from "@trpc/server/adapters/express";
import { registerOAuthRoutes } from "../backend/_core/oauth";
import { registerStorageProxy } from "../backend/_core/storageProxy";
import { appRouter } from "../backend/routers";
import { createContext } from "../backend/_core/context";
import { registerPathfinderRestRoutes } from "../backend/restRoutes";

const app = express();

app.use(express.json({ limit: "50mb" }));
app.use(express.urlencoded({ limit: "50mb", extended: true }));

// Normalize URL prefix for Vercel Serverless Function routing
app.use((req, _res, next) => {
  if (!req.url.startsWith("/api")) {
    req.url = `/api${req.url}`;
  }
  next();
});

registerStorageProxy(app);
registerOAuthRoutes(app);
registerPathfinderRestRoutes(app);

app.use(
  "/api/trpc",
  createExpressMiddleware({
    router: appRouter,
    createContext,
  })
);

export default app;
