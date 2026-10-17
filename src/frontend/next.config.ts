import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  experimental: {
    serverActions: {
      // Uploads pass through a Server Action; allow the API's 50 MB limit plus form overhead
      bodySizeLimit: "51mb",
    },
  },
};

export default nextConfig;
