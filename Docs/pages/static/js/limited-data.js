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
      },
      {
        "sha": "eab4511c710de4cb70ad69b2ce49a5bc70806e42",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:06:19 2026 \u002B0200",
        "message": "feat: add runtime arguments via \u0060[Arg]\u0060 and \u0060Func\u003CTArg\u2026,T\u003E\u0060 (#26)"
      },
      {
        "sha": "9a8f7c94f430232d4565721469710b174d60451c",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:53:42 2026 \u002B0200",
        "message": "refactor: Static \u0060[Container]\u0060 definition with a \u0060Root\u0060 instance (#27)"
      },
      {
        "sha": "9131a5ca97412f8d36cf3bb2a4777c7a93ae92d9",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 15:25:50 2026 \u002B0200",
        "message": "feat: add keyed registration via \u0060Key\u0060 and \u0060[FromKey]\u0060 (AWT116) (#28)"
      },
      {
        "sha": "ace7cc4813aa3beeee11c20eaa72252bf49f5492",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 16:06:45 2026 \u002B0200",
        "message": "feat: leak-free disposal \u2014 \u0060Owned\u003CT\u003E\u0060, flow-based AWT117, and strict-by-default \u0060LifetimeSafety\u0060 (#29)"
      },
      {
        "sha": "d9374cb3dc08d595b4023f32dda06adbaf380e7f",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 17:01:54 2026 \u002B0200",
        "message": "feat: allow scope-bound resolution of disposable transients under strict lifetime safety (#30)"
      },
      {
        "sha": "5cbed2e10da73610d193278d124c51cb792e8afa",
        "author": "dependabot[bot]",
        "date": "Mon Jun 29 11:12:47 2026 \u002B0200",
        "message": "chore: Bump SimpleInjector from 5.5.2 to 5.6.0 (#32)"
      },
      {
        "sha": "448c1d0d6cb6f88a36568d81925e9f6424705251",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 12:16:06 2026 \u002B0200",
        "message": "feat: add async initialization via \u0060IAsyncInitializable\u0060 with compile-time taint safety (AWT119/AWT120) (#31)"
      },
      {
        "sha": "d00d448cd3dda5bfd4bd934c3b1599d4a0267e46",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 15:12:14 2026 \u002B0200",
        "message": "fix: dispose factory outputs hidden behind a non-disposable return type (#33)"
      },
      {
        "sha": "14c9c203275f14878dcff01fba0253cc6da5fbcf",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 17:04:45 2026 \u002B0200",
        "message": "feat: add async factory methods as a Task\u003CT\u003E async-init registration channel (#34)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817",
      "eab4511c",
      "9a8f7c94",
      "9131a5ca",
      "ace7cc48",
      "d9374cb3",
      "5cbed2e1",
      "448c1d0d",
      "d00d448c",
      "14c9c203"
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
          12.485818333350695,
          12.543156417707602,
          9.96964891453584,
          11.049054309725761,
          12.466389536857605,
          13.950111746788025,
          12.077973314693995,
          7.0214919064726145,
          13.329308345913887,
          9.94473067795237
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
          0,
          0,
          0,
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
          7.2267070685823755,
          7.261836590675207,
          6.446227640977928,
          10.795238981644312,
          7.217046568791072,
          7.730148288820471,
          7.507831785541314,
          3.876718392861741,
          7.246332581226643,
          6.729099588734763
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
          0,
          0,
          0,
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
          111.30470051084247,
          105.91575457255045,
          89.3119827111562,
          144.3351561864217,
          128.3510533173879,
          121.71272279421488,
          121.84686679840088,
          58.20619033064161,
          118.74589716593424,
          86.48368591467539
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
          656,
          656,
          656,
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
          3.103375408798456,
          2.6501763961636104,
          2.4571942974414145,
          2.648928646619121,
          2.6137749309341114,
          2.6475117721905312,
          3.1994400146816457,
          1.4742107311529773,
          2.657380620141824,
          2.0131976918450425
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
          0,
          0,
          0,
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
          5.479966913278286,
          6.081691994116857,
          4.000388546784719,
          5.424282823290143,
          5.471353687345982,
          5.4598014400555535,
          4.903178572654724,
          2.140756678581238,
          5.423803340643644,
          3.9504249627391497
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
          0,
          0,
          0,
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
          9.338485512350287,
          9.06179791688919,
          6.813316751271486,
          9.382571436464787,
          8.677686004589,
          8.439247665496973,
          8.365555970619122,
          4.469606745243072,
          9.084253501433592,
          6.5100440363089245
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
          0,
          0,
          0,
          0,
          0,
          0,
          0,
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
          11.248150349905094,
          10.935928273413863,
          8.618113020062447,
          10.954928493925504,
          10.963660544157028,
          10.89521744961922,
          10.565347705284754,
          5.889249681362084,
          10.744728688682828,
          8.11381998558839
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
          0,
          0,
          0,
          0,
          0,
          0,
          0,
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
      },
      {
        "sha": "eab4511c710de4cb70ad69b2ce49a5bc70806e42",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:06:19 2026 \u002B0200",
        "message": "feat: add runtime arguments via \u0060[Arg]\u0060 and \u0060Func\u003CTArg\u2026,T\u003E\u0060 (#26)"
      },
      {
        "sha": "9a8f7c94f430232d4565721469710b174d60451c",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:53:42 2026 \u002B0200",
        "message": "refactor: Static \u0060[Container]\u0060 definition with a \u0060Root\u0060 instance (#27)"
      },
      {
        "sha": "9131a5ca97412f8d36cf3bb2a4777c7a93ae92d9",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 15:25:50 2026 \u002B0200",
        "message": "feat: add keyed registration via \u0060Key\u0060 and \u0060[FromKey]\u0060 (AWT116) (#28)"
      },
      {
        "sha": "ace7cc4813aa3beeee11c20eaa72252bf49f5492",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 16:06:45 2026 \u002B0200",
        "message": "feat: leak-free disposal \u2014 \u0060Owned\u003CT\u003E\u0060, flow-based AWT117, and strict-by-default \u0060LifetimeSafety\u0060 (#29)"
      },
      {
        "sha": "d9374cb3dc08d595b4023f32dda06adbaf380e7f",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 17:01:54 2026 \u002B0200",
        "message": "feat: allow scope-bound resolution of disposable transients under strict lifetime safety (#30)"
      },
      {
        "sha": "5cbed2e10da73610d193278d124c51cb792e8afa",
        "author": "dependabot[bot]",
        "date": "Mon Jun 29 11:12:47 2026 \u002B0200",
        "message": "chore: Bump SimpleInjector from 5.5.2 to 5.6.0 (#32)"
      },
      {
        "sha": "448c1d0d6cb6f88a36568d81925e9f6424705251",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 12:16:06 2026 \u002B0200",
        "message": "feat: add async initialization via \u0060IAsyncInitializable\u0060 with compile-time taint safety (AWT119/AWT120) (#31)"
      },
      {
        "sha": "d00d448cd3dda5bfd4bd934c3b1599d4a0267e46",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 15:12:14 2026 \u002B0200",
        "message": "fix: dispose factory outputs hidden behind a non-disposable return type (#33)"
      },
      {
        "sha": "14c9c203275f14878dcff01fba0253cc6da5fbcf",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 17:04:45 2026 \u002B0200",
        "message": "feat: add async factory methods as a Task\u003CT\u003E async-init registration channel (#34)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817",
      "eab4511c",
      "9a8f7c94",
      "9131a5ca",
      "ace7cc48",
      "d9374cb3",
      "5cbed2e1",
      "448c1d0d",
      "d00d448c",
      "14c9c203"
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
          25.132089374462762,
          21.429423230389755,
          13.951863878965378,
          17.636876922387344,
          420.81047419139315,
          417.1649808883667,
          209.15268858841486,
          168.13000315030416,
          421.2710775931676,
          360.4253009649423
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
          0,
          0,
          0,
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
          7.247819843036788,
          7.227084379929763,
          6.321874869863192,
          7.222078411706856,
          7.301884922843713,
          8.060321622093518,
          7.49612561861674,
          3.8394706931251745,
          7.246883647946211,
          6.274741545816263
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
          0,
          0,
          0,
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
          108.76541598637898,
          109.38529453277587,
          87.82475246985753,
          143.66148517813002,
          126.75394940376282,
          111.95694800217946,
          134.23758692741393,
          62.166154943979706,
          108.89532701969146,
          89.05647420883179
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
          656,
          656,
          656,
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
          42.71556845536599,
          42.674446195364,
          36.39172152536256,
          42.69592631780184,
          46.51291641822228,
          42.65599738061428,
          82.49723556836446,
          61.825484266647926,
          42.67858183383942,
          35.89213139244488
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
          0,
          0,
          0,
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
          8.246568957200417,
          8.27115285769105,
          6.638024993737539,
          8.268349653908185,
          8.228095612923305,
          8.895576333770386,
          6.984668902556101,
          3.3218329027295113,
          7.644824731562819,
          6.151889939393316
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
          0,
          0,
          0,
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
          9.386730642272877,
          9.023658861716589,
          6.819899412492911,
          9.355046457477979,
          8.74825328015364,
          8.400560635541167,
          8.347892115158695,
          4.367607290049394,
          9.032711013087205,
          6.483663130303224
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
          0,
          0,
          0,
          0,
          0,
          0,
          0,
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
          15.149906641244888,
          14.391228436277462,
          8.662876811623573,
          14.377744039663902,
          14.634206453959147,
          14.6518174101909,
          10.62877475044557,
          5.8207663503976965,
          14.061369708606176,
          8.246712926243033
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
          0,
          0,
          0,
          0,
          0,
          0,
          0,
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
      },
      {
        "sha": "eab4511c710de4cb70ad69b2ce49a5bc70806e42",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:06:19 2026 \u002B0200",
        "message": "feat: add runtime arguments via \u0060[Arg]\u0060 and \u0060Func\u003CTArg\u2026,T\u003E\u0060 (#26)"
      },
      {
        "sha": "9a8f7c94f430232d4565721469710b174d60451c",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:53:42 2026 \u002B0200",
        "message": "refactor: Static \u0060[Container]\u0060 definition with a \u0060Root\u0060 instance (#27)"
      },
      {
        "sha": "9131a5ca97412f8d36cf3bb2a4777c7a93ae92d9",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 15:25:50 2026 \u002B0200",
        "message": "feat: add keyed registration via \u0060Key\u0060 and \u0060[FromKey]\u0060 (AWT116) (#28)"
      },
      {
        "sha": "ace7cc4813aa3beeee11c20eaa72252bf49f5492",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 16:06:45 2026 \u002B0200",
        "message": "feat: leak-free disposal \u2014 \u0060Owned\u003CT\u003E\u0060, flow-based AWT117, and strict-by-default \u0060LifetimeSafety\u0060 (#29)"
      },
      {
        "sha": "d9374cb3dc08d595b4023f32dda06adbaf380e7f",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 17:01:54 2026 \u002B0200",
        "message": "feat: allow scope-bound resolution of disposable transients under strict lifetime safety (#30)"
      },
      {
        "sha": "5cbed2e10da73610d193278d124c51cb792e8afa",
        "author": "dependabot[bot]",
        "date": "Mon Jun 29 11:12:47 2026 \u002B0200",
        "message": "chore: Bump SimpleInjector from 5.5.2 to 5.6.0 (#32)"
      },
      {
        "sha": "448c1d0d6cb6f88a36568d81925e9f6424705251",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 12:16:06 2026 \u002B0200",
        "message": "feat: add async initialization via \u0060IAsyncInitializable\u0060 with compile-time taint safety (AWT119/AWT120) (#31)"
      },
      {
        "sha": "d00d448cd3dda5bfd4bd934c3b1599d4a0267e46",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 15:12:14 2026 \u002B0200",
        "message": "fix: dispose factory outputs hidden behind a non-disposable return type (#33)"
      },
      {
        "sha": "14c9c203275f14878dcff01fba0253cc6da5fbcf",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 17:04:45 2026 \u002B0200",
        "message": "feat: add async factory methods as a Task\u003CT\u003E async-init registration channel (#34)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817",
      "eab4511c",
      "9a8f7c94",
      "9131a5ca",
      "ace7cc48",
      "d9374cb3",
      "5cbed2e1",
      "448c1d0d",
      "d00d448c",
      "14c9c203"
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
          7.316113633910815,
          6.614255565290268,
          16.80864946757044,
          17.341485847319877,
          16.20428158768586,
          16.752829509973527,
          16.80542136303016,
          17.658237477143604,
          17.020056669528667,
          17.30248032084533
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
          32,
          32,
          136,
          136,
          136,
          136,
          136,
          136,
          136,
          136
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
          1483.7784776687622,
          1567.7728815714518,
          1495.9271629040059,
          1552.0977100372315,
          1459.548864364624,
          1526.6339089711507,
          1522.460163752238,
          1643.2737452189128,
          1572.3274399684026,
          1541.23062210083
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
          5688,
          5688,
          5688,
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
          29267.708498128257,
          30294.09384765625,
          28766.884537760416,
          29173.879069737024,
          28223.022371419273,
          28612.42024536133,
          29426.723600260415,
          30645.188363211495,
          30487.194295247395,
          28499.42884172712
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
          33093,
          33098,
          33098,
          33093,
          33098,
          33093,
          33098,
          33098,
          33098,
          33098
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
          9.32175682981809,
          8.751394224281494,
          11.581095778025114,
          14.421707503994305,
          12.430758961041768,
          8.850676357746124,
          10.244882452487946,
          8.119333387414615,
          8.111471297485489,
          12.249823608001073
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
          96,
          96,
          96,
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
          14.704782847847257,
          15.86008302172025,
          15.803155903021494,
          15.255123564830193,
          15.199766554435094,
          15.236377342542012,
          15.167793375253677,
          17.739024041096368,
          15.816561581833023,
          15.84739500284195
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
          128,
          128,
          128,
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
          812.0051765441895,
          754.9205152511597,
          709.1222532908122,
          669.355822631291,
          717.5703266779582,
          719.1566054270818,
          741.0764004389445,
          751.0916701634725,
          744.2354890278408,
          736.2870725631714
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
          1472,
          1472,
          1472,
          1472,
          1472,
          1528,
          1472,
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
          11499.226316179547,
          12021.721975199382,
          12044.885684204102,
          12057.189397176107,
          12053.583043416342,
          11875.420430501303,
          11564.490824018207,
          12849.747713216146,
          11727.152456665039,
          11936.07470957438
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
          24761,
          24760,
          24760,
          24760,
          24761,
          24760,
          24760,
          24761,
          24760,
          24760
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
      },
      {
        "sha": "eab4511c710de4cb70ad69b2ce49a5bc70806e42",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:06:19 2026 \u002B0200",
        "message": "feat: add runtime arguments via \u0060[Arg]\u0060 and \u0060Func\u003CTArg\u2026,T\u003E\u0060 (#26)"
      },
      {
        "sha": "9a8f7c94f430232d4565721469710b174d60451c",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:53:42 2026 \u002B0200",
        "message": "refactor: Static \u0060[Container]\u0060 definition with a \u0060Root\u0060 instance (#27)"
      },
      {
        "sha": "9131a5ca97412f8d36cf3bb2a4777c7a93ae92d9",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 15:25:50 2026 \u002B0200",
        "message": "feat: add keyed registration via \u0060Key\u0060 and \u0060[FromKey]\u0060 (AWT116) (#28)"
      },
      {
        "sha": "ace7cc4813aa3beeee11c20eaa72252bf49f5492",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 16:06:45 2026 \u002B0200",
        "message": "feat: leak-free disposal \u2014 \u0060Owned\u003CT\u003E\u0060, flow-based AWT117, and strict-by-default \u0060LifetimeSafety\u0060 (#29)"
      },
      {
        "sha": "d9374cb3dc08d595b4023f32dda06adbaf380e7f",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 17:01:54 2026 \u002B0200",
        "message": "feat: allow scope-bound resolution of disposable transients under strict lifetime safety (#30)"
      },
      {
        "sha": "5cbed2e10da73610d193278d124c51cb792e8afa",
        "author": "dependabot[bot]",
        "date": "Mon Jun 29 11:12:47 2026 \u002B0200",
        "message": "chore: Bump SimpleInjector from 5.5.2 to 5.6.0 (#32)"
      },
      {
        "sha": "448c1d0d6cb6f88a36568d81925e9f6424705251",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 12:16:06 2026 \u002B0200",
        "message": "feat: add async initialization via \u0060IAsyncInitializable\u0060 with compile-time taint safety (AWT119/AWT120) (#31)"
      },
      {
        "sha": "d00d448cd3dda5bfd4bd934c3b1599d4a0267e46",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 15:12:14 2026 \u002B0200",
        "message": "fix: dispose factory outputs hidden behind a non-disposable return type (#33)"
      },
      {
        "sha": "14c9c203275f14878dcff01fba0253cc6da5fbcf",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 17:04:45 2026 \u002B0200",
        "message": "feat: add async factory methods as a Task\u003CT\u003E async-init registration channel (#34)"
      }
    ],
    "labels": [
      "9e826c17",
      "c39ceed4",
      "3de2a7f9",
      "d3d0f306",
      "940e8190",
      "89165d69",
      "7f82b817",
      "eab4511c",
      "9a8f7c94",
      "9131a5ca",
      "ace7cc48",
      "d9374cb3",
      "5cbed2e1",
      "448c1d0d",
      "d00d448c",
      "14c9c203"
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
          11.901007292668025,
          6.337685420115789,
          106.67608408927917,
          95.65125444730123,
          93.34643878142039,
          93.10815994739532,
          85.79041192164787,
          79.82186423880714,
          80.78922328948974,
          98.35140030384063
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
          32,
          32,
          2120,
          2120,
          2120,
          2120,
          2120,
          2120,
          2120,
          2120
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
          14671.67342224121,
          14540.832564217704,
          15160.101663208008,
          16052.910624912807,
          14488.164255777994,
          14535.938489641461,
          14393.28782450358,
          14720.511655535016,
          15014.547132364909,
          14848.385481770832
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
          61016,
          61016,
          61016,
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
          695650.6519252232,
          714615.9444110577,
          694057.6142578125,
          768457.372907366,
          700853.3291666667,
          689787.6517159598,
          707332.1415364583,
          733951.2293419471,
          720016.024609375,
          711639.283063616
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
          738869,
          720154,
          720528,
          719330,
          739577,
          721188,
          740167,
          739569,
          738243,
          741807
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
          78.57072967688242,
          70.74225007692972,
          80.09677527745565,
          90.72007772127787,
          76.5894897143046,
          82.55766484737396,
          77.10259646177292,
          67.5593192021052,
          69.35875632365544,
          80.34384400049845
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
          2080,
          2080,
          2080,
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
          92.81633064576558,
          77.65065294504166,
          90.94009231726328,
          103.11985056400299,
          81.50612357029549,
          87.8775463955743,
          81.44898611765642,
          80.68386101722717,
          75.75164662996927,
          85.13619006474813
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
          2112,
          2112,
          2112,
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
          44658.55329182943,
          47098.63555908203,
          46157.830234781904,
          45255.52286001352,
          42872.15471942608,
          44626.313188825334,
          47249.81603597005,
          48318.182525634766,
          46568.499259440105,
          43908.69982038225
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
          80141,
          81808,
          81801,
          80840,
          80414,
          81762,
          80414,
          80412,
          80119,
          79176
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
          361583.29130859376,
          364420.01630859374,
          377650.2012765067,
          384195.8057942708,
          361491.06233723956,
          361124.86650390626,
          349293.7124399039,
          375361.7107872596,
          358326.2776692708,
          351901.6476888021
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
          573139,
          573125,
          572993,
          573036,
          573171,
          573057,
          573122,
          573057,
          573158,
          573220
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
      },
      {
        "sha": "eab4511c710de4cb70ad69b2ce49a5bc70806e42",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:06:19 2026 \u002B0200",
        "message": "feat: add runtime arguments via \u0060[Arg]\u0060 and \u0060Func\u003CTArg\u2026,T\u003E\u0060 (#26)"
      },
      {
        "sha": "9a8f7c94f430232d4565721469710b174d60451c",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 11:53:42 2026 \u002B0200",
        "message": "refactor: Static \u0060[Container]\u0060 definition with a \u0060Root\u0060 instance (#27)"
      },
      {
        "sha": "9131a5ca97412f8d36cf3bb2a4777c7a93ae92d9",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 15:25:50 2026 \u002B0200",
        "message": "feat: add keyed registration via \u0060Key\u0060 and \u0060[FromKey]\u0060 (AWT116) (#28)"
      },
      {
        "sha": "ace7cc4813aa3beeee11c20eaa72252bf49f5492",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 16:06:45 2026 \u002B0200",
        "message": "feat: leak-free disposal \u2014 \u0060Owned\u003CT\u003E\u0060, flow-based AWT117, and strict-by-default \u0060LifetimeSafety\u0060 (#29)"
      },
      {
        "sha": "d9374cb3dc08d595b4023f32dda06adbaf380e7f",
        "author": "Valentin Breu\u00DF",
        "date": "Sun Jun 28 17:01:54 2026 \u002B0200",
        "message": "feat: allow scope-bound resolution of disposable transients under strict lifetime safety (#30)"
      },
      {
        "sha": "5cbed2e10da73610d193278d124c51cb792e8afa",
        "author": "dependabot[bot]",
        "date": "Mon Jun 29 11:12:47 2026 \u002B0200",
        "message": "chore: Bump SimpleInjector from 5.5.2 to 5.6.0 (#32)"
      },
      {
        "sha": "448c1d0d6cb6f88a36568d81925e9f6424705251",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 12:16:06 2026 \u002B0200",
        "message": "feat: add async initialization via \u0060IAsyncInitializable\u0060 with compile-time taint safety (AWT119/AWT120) (#31)"
      },
      {
        "sha": "d00d448cd3dda5bfd4bd934c3b1599d4a0267e46",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 15:12:14 2026 \u002B0200",
        "message": "fix: dispose factory outputs hidden behind a non-disposable return type (#33)"
      },
      {
        "sha": "14c9c203275f14878dcff01fba0253cc6da5fbcf",
        "author": "Valentin Breu\u00DF",
        "date": "Mon Jun 29 17:04:45 2026 \u002B0200",
        "message": "feat: add async factory methods as a Task\u003CT\u003E async-init registration channel (#34)"
      }
    ],
    "labels": [
      "940e8190",
      "89165d69",
      "7f82b817",
      "eab4511c",
      "9a8f7c94",
      "9131a5ca",
      "ace7cc48",
      "d9374cb3",
      "5cbed2e1",
      "448c1d0d",
      "d00d448c",
      "14c9c203"
    ],
    "datasets": [
      {
        "label": "Awaiten time",
        "unit": "ns",
        "data": [
          300.9296982969557,
          281.76424653189525,
          252.6644298689706,
          260.1775264556591,
          251.63929040091378,
          265.12411315100536,
          256.6688412030538,
          237.4548293522426,
          235.024027188619,
          257.52146269480386,
          243.82445979118347,
          242.4582004547119
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
          528,
          528,
          560,
          560,
          560,
          560,
          560,
          560,
          560,
          560
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
          621.4196859768459,
          604.5278740882874,
          567.0787385304769,
          685.8862778799875,
          657.8339473860605,
          585.6002210889544,
          682.1355609893799,
          573.7770374161856,
          596.1200730250432,
          603.9705275808062
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
          1104,
          1104,
          1104,
          1104,
          1104,
          1104,
          1104,
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
          8792.644225056965,
          8489.64879506429,
          7898.239800673265,
          7078.697401428222,
          6981.757572174072,
          7988.286344909668,
          7936.628978474935,
          8346.48178100586,
          8121.36154683431,
          8210.38421529134
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
          13696,
          13696,
          13696,
          13696,
          13696,
          13696,
          13696,
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
          186.54834044774373,
          195.59591685022627,
          175.89961802164714,
          211.62491405010223,
          201.48101671536764,
          174.64754633903505,
          176.6947569676808,
          193.4218867301941,
          183.30500058027414,
          183.32636133829752
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
          432,
          432,
          432,
          432,
          432,
          432,
          432,
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
          415.5776258982145,
          447.2067368711744,
          383.6606852531433,
          405.90302689870197,
          396.8794637066977,
          390.2783552487691,
          397.27810700734454,
          430.9369169643947,
          390.46946674982706,
          402.31659008661904
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
          944,
          944,
          944,
          944,
          944,
          944,
          944,
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
          746.6466583524432,
          698.3003832272121,
          711.7773325783866,
          762.8543266296386,
          730.7787289937337,
          719.5856292724609,
          729.358271085299,
          679.5699736277262,
          742.7681127694937,
          722.7863157590231
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
          1096,
          1096,
          1096,
          1096,
          1096,
          1096,
          1096,
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
          200.69161790211996,
          194.3680850982666,
          178.99927331606548,
          175.31213323275247,
          176.94375816413336,
          177.65927648544312,
          172.98032293319702,
          191.6788766860962,
          175.87166377476282,
          183.12011408805847
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
          632,
          632,
          632,
          632,
          632,
          632,
          632,
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