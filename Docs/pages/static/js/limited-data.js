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
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190"
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
          13.648185913379375
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
          7.822928451001644
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
          119.50599819819132
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
          2.6472268807036534
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
          5.4428953776756925
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
          8.923961407194534
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
          10.943431161344051
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
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190"
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
          14.193635863917214
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
          8.285667914152146
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
          126.16176304817199
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
          42.65593960881233
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
          7.6472257146468525
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
          8.721771665981837
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
          14.636519505509309
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
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190"
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
          18.650352674722672
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
          160
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
          1149.1580527169365
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
          22899.955212402343
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
          33094
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
          6.439407130579154
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
          12.377571320533752
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
          553.2217806816101
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
          8830.186895751953
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
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190"
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
          63.68630854572569
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
          2144
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
          10451.63974202474
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
          538607.91796875
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
          720534
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
          50.734790054957074
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
          56.831977537700105
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
          35030.01636962891
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
          81806
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
          263164.6458984375
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
          573040
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
      }
    ],
    "labels": [
      "940e8190"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          300.9296982969557
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
          632
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
          624.5027207692464
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
          8328.710942949567
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
          182.09866827329
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
          405.80906445185343
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
          747.826828511556
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
          167.61310491959253
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