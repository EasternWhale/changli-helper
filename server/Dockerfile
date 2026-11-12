ARG NODE_IMAGE=node:24-alpine
FROM ${NODE_IMAGE}
WORKDIR /app
COPY server.cjs index.html app.js ./
USER node
EXPOSE 8766
CMD ["node", "server.cjs"]
