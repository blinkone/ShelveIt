// Browser preview of the panel. Separate from the mod build: nothing here reaches UI/build.
// From the UI folder:
//   node preview/tools/extract-classes.js "<game UI folder>" preview/data/classes.json
//   node preview/tools/make-sample-fixtures.js       (synthetic data; or make-fixtures.js from your own
//                                                     local scan, which must never be committed)
//   npx webpack --config preview/webpack.preview.js
//   node preview/serve.js          then open http://localhost:5517/
const path = require("path");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");

const ui = path.join(__dirname, "..");
const mock = (name) => path.join(__dirname, "mocks", name);

module.exports = {
  mode: "development",
  devtool: false,
  stats: "errors-warnings",
  context: ui,
  entry: { preview: "./preview/main.tsx" },
  output: { path: path.join(__dirname, "dist"), filename: "[name].js", publicPath: "/dist/", clean: true },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: [path.join(ui, "src"), path.join(ui, "node_modules")],
    alias: {
      "cs2/api$": mock("api.ts"),
      "cs2/ui$": mock("ui.tsx"),
      "cs2/l10n$": mock("l10n.ts"),
      "cs2/modding$": mock("modding.tsx"),
    },
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        exclude: /node_modules/,
        use: {
          loader: "ts-loader",
          options: { configFile: path.join(__dirname, "tsconfig.json"), transpileOnly: true },
        },
      },
      {
        test: /\.s?css$/,
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              url: true,
              importLoaders: 1,
              modules: { auto: true, exportLocalsConvention: "camelCase", localIdentName: "[local]_[hash:base64:3]" },
            },
          },
          { loader: "sass-loader", options: { api: "modern" } },
        ],
      },
      { test: /\.(png|jpe?g|gif|svg)$/i, type: "asset/resource", generator: { filename: "images/[name][ext][query]" } },
    ],
  },
  plugins: [new MiniCssExtractPlugin({ filename: "[name].css" })],
  performance: { hints: false },
};
