# Movies Assistant

A small movie-finder with a React + TypeScript UI and an ASP.NET Core API. OpenRouter handles chat and model routing; Jev, called through OpenRouter's Decisions API, screens the latest user message for prompt injection. Movie search/count tools query the local catalogue, while Langfuse can trace requests and the model selected by Jev Router.

The flow: the UI posts to `/chat`, the API validates the request and checks the latest user turn with Jev, then sends the conversation to the model. The model can call the movie tools, and the API streams its final reply back to the UI. Promptfoo evals in `evals/` cover search, recommendations, grounding, validation, and injection.

For local dev, set `OpenRouter:ApiKey` with `dotnet user-secrets set "OpenRouter:ApiKey" "<your-key>"` from `MovieAssistant.Api/`, then run `dotnet run`. In another terminal, run `npm install` and `npm run dev` from `movies-assistant-ui/`. Run evals with `npx promptfoo eval --no-cache` from `evals/`.