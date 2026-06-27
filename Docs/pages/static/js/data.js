window.BENCHMARK_DATA = {
  "Resolve (Size=8)": {
    "commits": [
      {
        "sha": "9e826c1752e5a3c03ff7c537e5a5ad00db4afacb",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 17:52:00 2026 \u002B0200",
        "message": "fix: resolve benchmark marker types and guard report publishing (#19)"
      },
      {
        "sha": "c39ceed4c71ce6d5ed1d6000eab48eaa5e4d49ee",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 18:42:24 2026 \u002B0200",
        "message": "perf: dispatch resolution through a static Type table instead of a linear if-chain (#20)"
      },
      {
        "sha": "3de2a7f96c56c3ea423fc6ac5f66b1494b53deb9",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:51:15 2026 \u002B0200",
        "message": "refactor: make benchmark classes self-contained and add DryIoc \u002B Simple Injector (#22)"
      },
      {
        "sha": "d3d0f30624d131d4c9ede97711bd191950aee0a5",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:53:13 2026 \u002B0200",
        "message": "feat: add \u0060Func\u003CT\u003E\u0060 and \u0060Lazy\u003CT\u003E\u0060 relationship types (#21)"
      },
      {
        "sha": "940e8190b6ed14fd96d348a2f312472178fe965f",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 21:05:55 2026 \u002B0200",
        "message": "feat: add realistic end-to-end resolution benchmark across DI containers (#23)"
      },
      {
        "sha": "89165d691f8960f3a4d8eb7f8d3b6110cdf41edf",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 22:12:46 2026 \u002B0200",
        "message": "feat: add factory-method and pre-built instance registrations (#24)"
      },
      {
        "sha": "7f82b81794374f2f164293ff3091260124ab6e60",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 00:25:11 2026 \u002B0200",
        "message": "refactor(generator): restructure generated container into a facade \u002B Scope/RootScope hierarchy (#25)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          6.042193595033425,
          9.984777721265951,
          9.885763876140118,
          9.773150008458357,
          13.648185913379375,
          11.08655926814446,
          12.485818333350695
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Awaiten memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "MsDI time",
        "unit": "ns",
        "data": [
          7.011932362403188,
          7.165684852462548,
          7.0664904532688,
          6.979647744160432,
          7.822928451001644,
          7.754035635718277,
          7.2267070685823755
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "MsDI memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Autofac time",
        "unit": "ns",
        "data": [
          106.18884980678558,
          105.09433993271419,
          113.55918945584979,
          109.79177531829247,
          119.50599819819132,
          125.12368343671163,
          111.30470051084247
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Autofac memory",
        "unit": "b",
        "data": [
          656,
          656,
          656,
          656,
          656,
          656,
          656
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Jab time",
        "unit": "ns",
        "data": [
          2.861889192213615,
          2.864032766116517,
          2.60612515732646,
          2.78809827967332,
          2.6472268807036534,
          2.6127634793519974,
          3.103375408798456
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Jab memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "PureDI time",
        "unit": "ns",
        "data": [
          6.725859894355138,
          6.806414321064949,
          6.40836979661669,
          6.734560783704122,
          5.4428953776756925,
          5.468037831996169,
          5.479966913278286
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "PureDI memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "DryIoc time",
        "unit": "ns",
        "data": [
          8.923961407194534,
          9.028634026646614,
          9.338485512350287
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "DryIoc memory",
        "unit": "b",
        "data": [
          0,
          0,
          0
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "SimpleInjector time",
        "unit": "ns",
        "data": [
          10.943431161344051,
          10.973706451746134,
          11.248150349905094
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "SimpleInjector memory",
        "unit": "b",
        "data": [
          0,
          0,
          0
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      }
    ]
  },
  "Resolve (Size=64)": {
    "commits": [
      {
        "sha": "9e826c1752e5a3c03ff7c537e5a5ad00db4afacb",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 17:52:00 2026 \u002B0200",
        "message": "fix: resolve benchmark marker types and guard report publishing (#19)"
      },
      {
        "sha": "c39ceed4c71ce6d5ed1d6000eab48eaa5e4d49ee",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 18:42:24 2026 \u002B0200",
        "message": "perf: dispatch resolution through a static Type table instead of a linear if-chain (#20)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          15.387899508078894,
          10.55480164984862
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Awaiten memory",
        "unit": "b",
        "data": [
          0,
          0
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "MsDI time",
        "unit": "ns",
        "data": [
          6.978684454091957,
          7.740073883533478
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "MsDI memory",
        "unit": "b",
        "data": [
          0,
          0
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Autofac time",
        "unit": "ns",
        "data": [
          110.4415994148988,
          109.30096944478842
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Autofac memory",
        "unit": "b",
        "data": [
          656,
          656
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Jab time",
        "unit": "ns",
        "data": [
          12.780041709542274,
          12.78639352780122
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Jab memory",
        "unit": "b",
        "data": [
          0,
          0
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "PureDI time",
        "unit": "ns",
        "data": [
          7.416174832444924,
          7.728808966966776
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "PureDI memory",
        "unit": "b",
        "data": [
          0,
          0
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      }
    ]
  },
  "Resolve (Size=256)": {
    "commits": [
      {
        "sha": "9e826c1752e5a3c03ff7c537e5a5ad00db4afacb",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 17:52:00 2026 \u002B0200",
        "message": "fix: resolve benchmark marker types and guard report publishing (#19)"
      },
      {
        "sha": "c39ceed4c71ce6d5ed1d6000eab48eaa5e4d49ee",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 18:42:24 2026 \u002B0200",
        "message": "perf: dispatch resolution through a static Type table instead of a linear if-chain (#20)"
      },
      {
        "sha": "3de2a7f96c56c3ea423fc6ac5f66b1494b53deb9",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:51:15 2026 \u002B0200",
        "message": "refactor: make benchmark classes self-contained and add DryIoc \u002B Simple Injector (#22)"
      },
      {
        "sha": "d3d0f30624d131d4c9ede97711bd191950aee0a5",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:53:13 2026 \u002B0200",
        "message": "feat: add \u0060Func\u003CT\u003E\u0060 and \u0060Lazy\u003CT\u003E\u0060 relationship types (#21)"
      },
      {
        "sha": "940e8190b6ed14fd96d348a2f312472178fe965f",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 21:05:55 2026 \u002B0200",
        "message": "feat: add realistic end-to-end resolution benchmark across DI containers (#23)"
      },
      {
        "sha": "89165d691f8960f3a4d8eb7f8d3b6110cdf41edf",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 22:12:46 2026 \u002B0200",
        "message": "feat: add factory-method and pre-built instance registrations (#24)"
      },
      {
        "sha": "7f82b81794374f2f164293ff3091260124ab6e60",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 00:25:11 2026 \u002B0200",
        "message": "refactor(generator): restructure generated container into a facade \u002B Scope/RootScope hierarchy (#25)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          45.455347631658825,
          11.401101275132252,
          10.104073643684387,
          14.171938593188921,
          14.193635863917214,
          14.478504076600075,
          25.132089374462762
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Awaiten memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "MsDI time",
        "unit": "ns",
        "data": [
          7.005394069211824,
          7.192912148741575,
          7.101283203278269,
          7.868850610085896,
          8.285667914152146,
          8.033807569742203,
          7.247819843036788
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "MsDI memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Autofac time",
        "unit": "ns",
        "data": [
          109.87674596905708,
          105.27381155320576,
          112.35390312331063,
          105.2306796948115,
          126.16176304817199,
          117.14112833340963,
          108.76541598637898
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Autofac memory",
        "unit": "b",
        "data": [
          656,
          656,
          656,
          656,
          656,
          656,
          656
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Jab time",
        "unit": "ns",
        "data": [
          42.81887869040171,
          42.793274948230156,
          42.812093526124954,
          42.77725352559771,
          42.65593960881233,
          42.700177299976346,
          42.71556845536599
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Jab memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "PureDI time",
        "unit": "ns",
        "data": [
          8.274077805188986,
          8.261776864528656,
          7.603359976640115,
          8.242683834754503,
          7.6472257146468525,
          7.289087125233242,
          8.246568957200417
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "PureDI memory",
        "unit": "b",
        "data": [
          0,
          0,
          0,
          0,
          0,
          0,
          0
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "DryIoc time",
        "unit": "ns",
        "data": [
          8.721771665981837,
          9.069725214288784,
          9.386730642272877
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "DryIoc memory",
        "unit": "b",
        "data": [
          0,
          0,
          0
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "SimpleInjector time",
        "unit": "ns",
        "data": [
          14.636519505509309,
          14.909030141574997,
          15.149906641244888
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "SimpleInjector memory",
        "unit": "b",
        "data": [
          0,
          0,
          0
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      }
    ]
  },
  "Build (Size=8)": {
    "commits": [
      {
        "sha": "9e826c1752e5a3c03ff7c537e5a5ad00db4afacb",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 17:52:00 2026 \u002B0200",
        "message": "fix: resolve benchmark marker types and guard report publishing (#19)"
      },
      {
        "sha": "c39ceed4c71ce6d5ed1d6000eab48eaa5e4d49ee",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 18:42:24 2026 \u002B0200",
        "message": "perf: dispatch resolution through a static Type table instead of a linear if-chain (#20)"
      },
      {
        "sha": "3de2a7f96c56c3ea423fc6ac5f66b1494b53deb9",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:51:15 2026 \u002B0200",
        "message": "refactor: make benchmark classes self-contained and add DryIoc \u002B Simple Injector (#22)"
      },
      {
        "sha": "d3d0f30624d131d4c9ede97711bd191950aee0a5",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:53:13 2026 \u002B0200",
        "message": "feat: add \u0060Func\u003CT\u003E\u0060 and \u0060Lazy\u003CT\u003E\u0060 relationship types (#21)"
      },
      {
        "sha": "940e8190b6ed14fd96d348a2f312472178fe965f",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 21:05:55 2026 \u002B0200",
        "message": "feat: add realistic end-to-end resolution benchmark across DI containers (#23)"
      },
      {
        "sha": "89165d691f8960f3a4d8eb7f8d3b6110cdf41edf",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 22:12:46 2026 \u002B0200",
        "message": "feat: add factory-method and pre-built instance registrations (#24)"
      },
      {
        "sha": "7f82b81794374f2f164293ff3091260124ab6e60",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 00:25:11 2026 \u002B0200",
        "message": "refactor(generator): restructure generated container into a facade \u002B Scope/RootScope hierarchy (#25)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          22.586610968907674,
          25.408868404229484,
          24.944836461544035,
          22.270263612270355,
          18.650352674722672,
          24.012085938453673,
          7.316113633910815
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Awaiten memory",
        "unit": "b",
        "data": [
          160,
          160,
          160,
          160,
          160,
          160,
          32
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "MsDI time",
        "unit": "ns",
        "data": [
          1650.7135983784995,
          1556.666805903117,
          1475.3557790120442,
          1515.808144124349,
          1149.1580527169365,
          1493.4126134236653,
          1483.7784776687622
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "MsDI memory",
        "unit": "b",
        "data": [
          5688,
          5688,
          5688,
          5688,
          5688,
          5688,
          5688
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Autofac time",
        "unit": "ns",
        "data": [
          24660.685026041665,
          28979.10302734375,
          28257.207649739583,
          28856.750139508928,
          22899.955212402343,
          29633.70913696289,
          29267.708498128257
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Autofac memory",
        "unit": "b",
        "data": [
          33092,
          33098,
          33098,
          33098,
          33094,
          33094,
          33093
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Jab time",
        "unit": "ns",
        "data": [
          10.445150269071261,
          9.20387484558991,
          9.388717264930408,
          9.305480483174325,
          6.439407130579154,
          8.72648409108321,
          9.32175682981809
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Jab memory",
        "unit": "b",
        "data": [
          96,
          96,
          96,
          96,
          96,
          96,
          96
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "PureDI time",
        "unit": "ns",
        "data": [
          16.12693266669909,
          15.165443106339527,
          15.121575939655305,
          15.329789812748249,
          12.377571320533752,
          15.526808367172878,
          14.704782847847257
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "PureDI memory",
        "unit": "b",
        "data": [
          128,
          128,
          128,
          128,
          128,
          128,
          128
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "DryIoc time",
        "unit": "ns",
        "data": [
          553.2217806816101,
          718.6632721083505,
          812.0051765441895
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "DryIoc memory",
        "unit": "b",
        "data": [
          1472,
          1472,
          1472
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "SimpleInjector time",
        "unit": "ns",
        "data": [
          8830.186895751953,
          11454.365837097168,
          11499.226316179547
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "SimpleInjector memory",
        "unit": "b",
        "data": [
          24761,
          24760,
          24761
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      }
    ]
  },
  "Build (Size=64)": {
    "commits": [
      {
        "sha": "9e826c1752e5a3c03ff7c537e5a5ad00db4afacb",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 17:52:00 2026 \u002B0200",
        "message": "fix: resolve benchmark marker types and guard report publishing (#19)"
      },
      {
        "sha": "c39ceed4c71ce6d5ed1d6000eab48eaa5e4d49ee",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 18:42:24 2026 \u002B0200",
        "message": "perf: dispatch resolution through a static Type table instead of a linear if-chain (#20)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          45.63413532574972,
          42.71426098163311
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Awaiten memory",
        "unit": "b",
        "data": [
          608,
          608
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "MsDI time",
        "unit": "ns",
        "data": [
          4279.234137471517,
          4191.629210408529
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "MsDI memory",
        "unit": "b",
        "data": [
          16336,
          16336
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Autofac time",
        "unit": "ns",
        "data": [
          135563.01016671318,
          167634.58933803014
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Autofac memory",
        "unit": "b",
        "data": [
          189845,
          190574
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Jab time",
        "unit": "ns",
        "data": [
          31.761847615242004,
          25.509863802364894
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Jab memory",
        "unit": "b",
        "data": [
          544,
          544
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "PureDI time",
        "unit": "ns",
        "data": [
          38.221518198649086,
          34.240006282925606
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "PureDI memory",
        "unit": "b",
        "data": [
          576,
          576
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      }
    ]
  },
  "Build (Size=256)": {
    "commits": [
      {
        "sha": "9e826c1752e5a3c03ff7c537e5a5ad00db4afacb",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 17:52:00 2026 \u002B0200",
        "message": "fix: resolve benchmark marker types and guard report publishing (#19)"
      },
      {
        "sha": "c39ceed4c71ce6d5ed1d6000eab48eaa5e4d49ee",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 18:42:24 2026 \u002B0200",
        "message": "perf: dispatch resolution through a static Type table instead of a linear if-chain (#20)"
      },
      {
        "sha": "3de2a7f96c56c3ea423fc6ac5f66b1494b53deb9",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:51:15 2026 \u002B0200",
        "message": "refactor: make benchmark classes self-contained and add DryIoc \u002B Simple Injector (#22)"
      },
      {
        "sha": "d3d0f30624d131d4c9ede97711bd191950aee0a5",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 19:53:13 2026 \u002B0200",
        "message": "feat: add \u0060Func\u003CT\u003E\u0060 and \u0060Lazy\u003CT\u003E\u0060 relationship types (#21)"
      },
      {
        "sha": "940e8190b6ed14fd96d348a2f312472178fe965f",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 21:05:55 2026 \u002B0200",
        "message": "feat: add realistic end-to-end resolution benchmark across DI containers (#23)"
      },
      {
        "sha": "89165d691f8960f3a4d8eb7f8d3b6110cdf41edf",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 22:12:46 2026 \u002B0200",
        "message": "feat: add factory-method and pre-built instance registrations (#24)"
      },
      {
        "sha": "7f82b81794374f2f164293ff3091260124ab6e60",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 00:25:11 2026 \u002B0200",
        "message": "refactor(generator): restructure generated container into a facade \u002B Scope/RootScope hierarchy (#25)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          115.49085221971784,
          93.44233902863094,
          95.15006294617287,
          103.8347438176473,
          63.68630854572569,
          82.76847733656565,
          11.901007292668025
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Awaiten memory",
        "unit": "b",
        "data": [
          2144,
          2144,
          2144,
          2144,
          2144,
          2144,
          32
        ],
        "borderColor": "#63A2AC",
        "backgroundColor": "#63A2AC",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "MsDI time",
        "unit": "ns",
        "data": [
          14105.811452229818,
          14303.591486249652,
          14221.36842549642,
          14621.441815185546,
          10451.63974202474,
          13686.143403116863,
          14671.67342224121
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "MsDI memory",
        "unit": "b",
        "data": [
          61016,
          61016,
          61016,
          61016,
          61016,
          61016,
          61016
        ],
        "borderColor": "#A052B0",
        "backgroundColor": "#A052B0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Autofac time",
        "unit": "ns",
        "data": [
          555859.5663311298,
          683072.6413225447,
          671868.4560546875,
          697260.7360677083,
          538607.91796875,
          700781.8356119791,
          695650.6519252232
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Autofac memory",
        "unit": "b",
        "data": [
          739452,
          741789,
          740145,
          739634,
          720534,
          721573,
          738869
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Jab time",
        "unit": "ns",
        "data": [
          104.94684716860453,
          79.59447942574819,
          79.5539099295934,
          73.27888635488657,
          50.734790054957074,
          66.36810166835785,
          78.57072967688242
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Jab memory",
        "unit": "b",
        "data": [
          2080,
          2080,
          2080,
          2080,
          2080,
          2080,
          2080
        ],
        "borderColor": "#4A6FA5",
        "backgroundColor": "#4A6FA5",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "PureDI time",
        "unit": "ns",
        "data": [
          115.83049867947896,
          89.94245272874832,
          81.5749951856477,
          90.04482505321502,
          56.831977537700105,
          74.6198572397232,
          92.81633064576558
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "PureDI memory",
        "unit": "b",
        "data": [
          2112,
          2112,
          2112,
          2112,
          2112,
          2112,
          2112
        ],
        "borderColor": "#FF8C00",
        "backgroundColor": "#FF8C00",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "DryIoc time",
        "unit": "ns",
        "data": [
          35030.01636962891,
          44238.90163312639,
          44658.55329182943
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "DryIoc memory",
        "unit": "b",
        "data": [
          81806,
          79999,
          80141
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "SimpleInjector time",
        "unit": "ns",
        "data": [
          263164.6458984375,
          360357.1323893229,
          361583.29130859376
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "SimpleInjector memory",
        "unit": "b",
        "data": [
          573040,
          573058,
          573139
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      }
    ]
  },
  "Realistic": {
    "commits": [
      {
        "sha": "940e8190b6ed14fd96d348a2f312472178fe965f",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 21:05:55 2026 \u002B0200",
        "message": "feat: add realistic end-to-end resolution benchmark across DI containers (#23)"
      },
      {
        "sha": "89165d691f8960f3a4d8eb7f8d3b6110cdf41edf",
        "author": "Valentin Breu\u00DF",
        "date": "Sat Jun 27 22:12:46 2026 \u002B0200",
        "message": "feat: add factory-method and pre-built instance registrations (#24)"
      },
      {
        "sha": "7f82b81794374f2f164293ff3091260124ab6e60",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 00:25:11 2026 \u002B0200",
        "message": "refactor(generator): restructure generated container into a facade \u002B Scope/RootScope hierarchy (#25)"
      }
    ],
    "labels": [
      "940e8190",
      "89165d69",
      "7f82b817"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          300.9296982969557,
          281.76424653189525,
          252.6644298689706
        ],
        "borderColor": "#3949AB",
        "backgroundColor": "#3949AB",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Awaiten memory",
        "unit": "b",
        "data": [
          632,
          632,
          528
        ],
        "borderColor": "#3949AB",
        "backgroundColor": "#3949AB",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "MsDI time",
        "unit": "ns",
        "data": [
          624.5027207692464,
          584.5357025146484,
          621.4196859768459
        ],
        "borderColor": "#512BD4",
        "backgroundColor": "#512BD4",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "MsDI memory",
        "unit": "b",
        "data": [
          1104,
          1104,
          1104
        ],
        "borderColor": "#512BD4",
        "backgroundColor": "#512BD4",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Autofac time",
        "unit": "ns",
        "data": [
          8328.710942949567,
          8260.446672712054,
          8792.644225056965
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Autofac memory",
        "unit": "b",
        "data": [
          13696,
          13696,
          13696
        ],
        "borderColor": "#5E2750",
        "backgroundColor": "#5E2750",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "Jab time",
        "unit": "ns",
        "data": [
          182.09866827329,
          176.86122382481892,
          186.54834044774373
        ],
        "borderColor": "#D9534F",
        "backgroundColor": "#D9534F",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "Jab memory",
        "unit": "b",
        "data": [
          432,
          432,
          432
        ],
        "borderColor": "#D9534F",
        "backgroundColor": "#D9534F",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "DryIoc time",
        "unit": "ns",
        "data": [
          405.80906445185343,
          394.3945826848348,
          415.5776258982145
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "DryIoc memory",
        "unit": "b",
        "data": [
          944,
          944,
          944
        ],
        "borderColor": "#1565C0",
        "backgroundColor": "#1565C0",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "SimpleInjector time",
        "unit": "ns",
        "data": [
          747.826828511556,
          712.3931697209676,
          746.6466583524432
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "SimpleInjector memory",
        "unit": "b",
        "data": [
          1096,
          1096,
          1096
        ],
        "borderColor": "#43A047",
        "backgroundColor": "#43A047",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      },
      {
        "label": "PureDI time",
        "unit": "ns",
        "data": [
          167.61310491959253,
          181.1375930627187,
          200.69161790211996
        ],
        "borderColor": "#F0AD4E",
        "backgroundColor": "#F0AD4E",
        "yAxisID": "y",
        "borderDash": [],
        "pointStyle": "circle"
      },
      {
        "label": "PureDI memory",
        "unit": "b",
        "data": [
          632,
          632,
          632
        ],
        "borderColor": "#F0AD4E",
        "backgroundColor": "#F0AD4E",
        "yAxisID": "y1",
        "borderDash": [
          5,
          5
        ],
        "pointStyle": "triangle"
      }
    ]
  }
}