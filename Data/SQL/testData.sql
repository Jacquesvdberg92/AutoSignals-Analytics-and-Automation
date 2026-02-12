-- Clear existing test data
DELETE FROM [AutoSignals].[dbo].[Orders] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d';

DELETE FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d';

-- Create 5 current test positions (OPEN)
INSERT INTO [AutoSignals].[dbo].[Positions] 
    ([UserId], [ExchangeId], [TelegramId], [Side], [Size], [Leverage], 
     [Symbol], [Entry], [Stoploss], [ROI], [Status], [Time], [IsTest], 
     [CloseTime], [EstLiquidation], [IsIsolated], [ClosePrice])
VALUES
    -- Position 1: BTC/USDT Long - Entry EXECUTED, others OPEN
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1001', 'buy', '0.75', 10, 
     'BTC/USDT:USDT', 52000.00, 50500.00, 3.2, 'OPEN', DATEADD(hour, -48, GETUTCDATE()), 1, 
     NULL, 48000.00, 0, NULL),
     
    -- Position 2: ETH/USDT Long - Entry EXECUTED, others OPEN 
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1002', 'buy', '5.25', 5, 
     'ETH/USDT:USDT', 2800.00, 2700.00, -1.8, 'OPEN', DATEADD(hour, -36, GETUTCDATE()), 1, 
     NULL, 2600.00, 0, NULL),
     
    -- Position 3: SOL/USDT Short - Entry EXECUTED, others OPEN
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1003', 'sell', '2500', 20, 
     'SOL/USDT:USDT', 110.00, 115.00, 5.5, 'OPEN', DATEADD(hour, -24, GETUTCDATE()), 1, 
     NULL, 120.00, 0, NULL),
     
    -- Position 4: ADA/USDT Long - Entry OPEN (not yet executed)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1004', 'buy', '12500', 8, 
     'ADA/USDT:USDT', 0.55, 0.52, 0.8, 'OPEN', DATEADD(hour, -12, GETUTCDATE()), 1, 
     NULL, 0.50, 0, NULL),
     
    -- Position 5: BNB/USDT Short - Entry OPEN (not yet executed)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1005', 'sell', '7.5', 15, 
     'BNB/USDT:USDT', 350.00, 360.00, -2.1, 'OPEN', DATEADD(hour, -6, GETUTCDATE()), 1, 
     NULL, 370.00, 0, NULL),

    -- Additional historical positions for date filtering tests:
    
    -- Position 6: BTC/USDT Long - CLOSED 7 days ago (via TP)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1006', 'buy', '1.5', 8, 
     'BTC/USDT:USDT', 49000.00, 47500.00, 8.5, 'CLOSED', DATEADD(day, -7, GETUTCDATE()), 1, 
     DATEADD(day, -6, GETUTCDATE()), 46000.00, 0, 53165.00),
     
    -- Position 7: ETH/USDT Short - CLOSED 15 days ago (via SL)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1007', 'sell', '10', 10, 
     'ETH/USDT:USDT', 3100.00, 3200.00, -3.2, 'CLOSED', DATEADD(day, -15, GETUTCDATE()), 1, 
     DATEADD(day, -14, GETUTCDATE()), 3300.00, 0, 3200.00),
     
    -- Position 8: SOL/USDT Long - CLOSED 30 days ago (via SL)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1008', 'buy', '5000', 15, 
     'SOL/USDT:USDT', 95.00, 90.00, -5.3, 'CLOSED', DATEADD(day, -30, GETUTCDATE()), 1, 
     DATEADD(day, -29, GETUTCDATE()), 85.00, 0, 90.00),
     
    -- Position 9: ADA/USDT Short - CLOSED 45 days ago (via TP)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1009', 'sell', '25000', 5, 
     'ADA/USDT:USDT', 0.60, 0.63, 6.7, 'CLOSED', DATEADD(day, -45, GETUTCDATE()), 1, 
     DATEADD(day, -44, GETUTCDATE()), 0.65, 0, 0.5598),
     
    -- Position 10: BNB/USDT Long - CLOSED 90 days ago (via TP)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1010', 'buy', '15', 12, 
     'BNB/USDT:USDT', 320.00, 310.00, 15.8, 'CLOSED', DATEADD(day, -90, GETUTCDATE()), 1, 
     DATEADD(day, -88, GETUTCDATE()), 300.00, 0, 370.56),
     
    -- Position 11: DOT/USDT Long - CLOSED 100 days ago (outside 90-day filter)
    ('3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1011', 'buy', '4000', 8, 
     'DOT/USDT:USDT', 7.50, 7.20, 22.4, 'CLOSED', DATEADD(day, -100, GETUTCDATE()), 1, 
     DATEADD(day, -98, GETUTCDATE()), 7.00, 0, 9.18);

-- Get the created position IDs as strings
DECLARE @btcPositionId NVARCHAR(MAX);
DECLARE @ethPositionId NVARCHAR(MAX);
DECLARE @solPositionId NVARCHAR(MAX);
DECLARE @adaPositionId NVARCHAR(MAX);
DECLARE @bnbPositionId NVARCHAR(MAX);
DECLARE @btcClosed7dPositionId NVARCHAR(MAX);
DECLARE @ethClosed15dPositionId NVARCHAR(MAX);
DECLARE @solClosed30dPositionId NVARCHAR(MAX);
DECLARE @adaClosed45dPositionId NVARCHAR(MAX);
DECLARE @bnbClosed90dPositionId NVARCHAR(MAX);
DECLARE @dotClosed100dPositionId NVARCHAR(MAX);

SELECT @btcPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'BTC/USDT:USDT' AND Status = 'OPEN' AND Entry = 52000.00;

SELECT @ethPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'ETH/USDT:USDT' AND Status = 'OPEN' AND Entry = 2800.00;

SELECT @solPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'SOL/USDT:USDT' AND Status = 'OPEN' AND Entry = 110.00;

SELECT @adaPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'ADA/USDT:USDT' AND Status = 'OPEN' AND Entry = 0.55;

SELECT @bnbPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'BNB/USDT:USDT' AND Status = 'OPEN' AND Entry = 350.00;

SELECT @btcClosed7dPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'BTC/USDT:USDT' AND Status = 'CLOSED' AND Entry = 49000.00;

SELECT @ethClosed15dPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'ETH/USDT:USDT' AND Status = 'CLOSED' AND Entry = 3100.00;

SELECT @solClosed30dPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'SOL/USDT:USDT' AND Status = 'CLOSED' AND Entry = 95.00;

SELECT @adaClosed45dPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'ADA/USDT:USDT' AND Status = 'CLOSED' AND Entry = 0.60;

SELECT @bnbClosed90dPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'BNB/USDT:USDT' AND Status = 'CLOSED' AND Entry = 320.00;

SELECT @dotClosed100dPositionId = CAST(Id AS NVARCHAR(MAX)) 
FROM [AutoSignals].[dbo].[Positions] 
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d' AND Symbol = 'DOT/USDT:USDT';

-- Create orders for BTC position (OPEN, entry EXECUTED)
INSERT INTO [AutoSignals].[dbo].[Orders] 
    ([SignalId], [UserId], [ExchangeId], [TelegramId], [PositionId], [UserName], 
     [Symbol], [Side], [Price], [Stoploss], [Size], [Leverage], [IsIsolated], 
     [IsTest], [Status], [Description], [Time])
VALUES
    -- BTC Order 1: Initial Entry Order (EXECUTED) - This triggered other orders to OPEN
    (1001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1001', @btcPositionId, 'TestUser',
     'BTC/USDT:USDT', 'buy', 52000.00, 50500.00, 0.75, 10, 0,
     1, 'EXECUTED', 'Initial Entry Order', DATEADD(hour, -48, GETUTCDATE())),
     
    -- BTC Order 2: DCA1 Entry Order (OPEN - ready to be filled)
    (1002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1001', @btcPositionId, 'TestUser',
     'BTC/USDT:USDT', 'buy', 50960.00, 50500.00, 0.30, 10, 0,
     1, 'OPEN', 'DCA1 Entry Order', DATEADD(hour, -47, GETUTCDATE())),
     
    -- BTC Order 3: DCA2 Entry Order (OPEN - ready to be filled)
    (1003, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1001', @btcPositionId, 'TestUser',
     'BTC/USDT:USDT', 'buy', 49920.00, 50500.00, 0.45, 10, 0,
     1, 'OPEN', 'DCA2 Entry Order', DATEADD(hour, -46, GETUTCDATE())),
     
    -- BTC Order 4: Stoploss Order (OPEN - active)
    (1004, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1001', @btcPositionId, 'TestUser',
     'BTC/USDT:USDT', 'sell', 50500.00, 50500.00, 1.5, 10, 0,
     1, 'OPEN', 'Stoploss Order', DATEADD(hour, -45, GETUTCDATE())),
     
    -- BTC Order 5: Take Profit Order 1 (OPEN - active)
    (1005, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1001', @btcPositionId, 'TestUser',
     'BTC/USDT:USDT', 'sell', 53560.00, 0, 0.6, 10, 0,
     1, 'OPEN', 'Take Profit Order 1', DATEADD(hour, -44, GETUTCDATE())),
     
    -- BTC Order 6: Take Profit Order 2 + MSL (OPEN - active)
    (1006, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1001', @btcPositionId, 'TestUser',
     'BTC/USDT:USDT', 'sell', 55120.00, 0, 0.9, 10, 0,
     1, 'OPEN', 'Take Profit Order 2 + MSL', DATEADD(hour, -43, GETUTCDATE()));

-- Create orders for ETH position (OPEN, entry EXECUTED)
INSERT INTO [AutoSignals].[dbo].[Orders] 
    ([SignalId], [UserId], [ExchangeId], [TelegramId], [PositionId], [UserName], 
     [Symbol], [Side], [Price], [Stoploss], [Size], [Leverage], [IsIsolated], 
     [IsTest], [Status], [Description], [Time])
VALUES
    -- ETH Order 1: Initial Entry Order (EXECUTED)
    (2001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1002', @ethPositionId, 'TestUser',
     'ETH/USDT:USDT', 'buy', 2800.00, 2700.00, 5.25, 5, 0,
     1, 'EXECUTED', 'Initial Entry Order', DATEADD(hour, -36, GETUTCDATE())),
     
    -- ETH Order 2: DCA1 Entry Order (OPEN)
    (2002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1002', @ethPositionId, 'TestUser',
     'ETH/USDT:USDT', 'buy', 2744.00, 2700.00, 2.10, 5, 0,
     1, 'OPEN', 'DCA1 Entry Order', DATEADD(hour, -35, GETUTCDATE())),
     
    -- ETH Order 3: DCA2 Entry Order (OPEN)
    (2003, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1002', @ethPositionId, 'TestUser',
     'ETH/USDT:USDT', 'buy', 2688.00, 2700.00, 3.15, 5, 0,
     1, 'OPEN', 'DCA2 Entry Order', DATEADD(hour, -34, GETUTCDATE())),
     
    -- ETH Order 4: Stoploss Order (OPEN)
    (2004, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1002', @ethPositionId, 'TestUser',
     'ETH/USDT:USDT', 'sell', 2700.00, 2700.00, 10.5, 5, 0,
     1, 'OPEN', 'Stoploss Order', DATEADD(hour, -33, GETUTCDATE())),
     
    -- ETH Order 5: Take Profit Order 1 (OPEN)
    (2005, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1002', @ethPositionId, 'TestUser',
     'ETH/USDT:USDT', 'sell', 2884.00, 0, 4.2, 5, 0,
     1, 'OPEN', 'Take Profit Order 1', DATEADD(hour, -32, GETUTCDATE())),
     
    -- ETH Order 6: Take Profit Order 2 + MSL (OPEN)
    (2006, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1002', @ethPositionId, 'TestUser',
     'ETH/USDT:USDT', 'sell', 2968.00, 0, 6.3, 5, 0,
     1, 'OPEN', 'Take Profit Order 2 + MSL', DATEADD(hour, -31, GETUTCDATE()));

-- Create orders for SOL position (OPEN) - SHORT, entry EXECUTED
INSERT INTO [AutoSignals].[dbo].[Orders] 
    ([SignalId], [UserId], [ExchangeId], [TelegramId], [PositionId], [UserName], 
     [Symbol], [Side], [Price], [Stoploss], [Size], [Leverage], [IsIsolated], 
     [IsTest], [Status], [Description], [Time])
VALUES
    -- SOL Order 1: Initial Entry Order (EXECUTED) - SHORT
    (3001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1003', @solPositionId, 'TestUser',
     'SOL/USDT:USDT', 'sell', 110.00, 115.00, 2500, 20, 0,
     1, 'EXECUTED', 'Initial Entry Order', DATEADD(hour, -24, GETUTCDATE())),
     
    -- SOL Order 2: DCA1 Entry Order (OPEN) - SHORT
    (3002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1003', @solPositionId, 'TestUser',
     'SOL/USDT:USDT', 'sell', 112.20, 115.00, 1000, 20, 0,
     1, 'OPEN', 'DCA1 Entry Order', DATEADD(hour, -23, GETUTCDATE())),
     
    -- SOL Order 3: DCA2 Entry Order (OPEN) - SHORT
    (3003, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1003', @solPositionId, 'TestUser',
     'SOL/USDT:USDT', 'sell', 114.40, 115.00, 1500, 20, 0,
     1, 'OPEN', 'DCA2 Entry Order', DATEADD(hour, -22, GETUTCDATE())),
     
    -- SOL Order 4: Stoploss Order (OPEN) - For SHORT this is a BUY
    (3004, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1003', @solPositionId, 'TestUser',
     'SOL/USDT:USDT', 'buy', 115.00, 115.00, 5000, 20, 0,
     1, 'OPEN', 'Stoploss Order', DATEADD(hour, -21, GETUTCDATE())),
     
    -- SOL Order 5: Take Profit Order 1 (OPEN) - For SHORT this is a BUY
    (3005, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1003', @solPositionId, 'TestUser',
     'SOL/USDT:USDT', 'buy', 106.70, 0, 2000, 20, 0,
     1, 'OPEN', 'Take Profit Order 1', DATEADD(hour, -20, GETUTCDATE())),
     
    -- SOL Order 6: Take Profit Order 2 + MSL (OPEN) - For SHORT this is a BUY
    (3006, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1003', @solPositionId, 'TestUser',
     'SOL/USDT:USDT', 'buy', 103.40, 0, 3000, 20, 0,
     1, 'OPEN', 'Take Profit Order 2 + MSL', DATEADD(hour, -19, GETUTCDATE()));

-- Create orders for ADA position (OPEN, entry NOT yet executed - PENDING orders for DCA/TP/SL)
INSERT INTO [AutoSignals].[dbo].[Orders] 
    ([SignalId], [UserId], [ExchangeId], [TelegramId], [PositionId], [UserName], 
     [Symbol], [Side], [Price], [Stoploss], [Size], [Leverage], [IsIsolated], 
     [IsTest], [Status], [Description], [Time])
VALUES
    -- ADA Order 1: Initial Entry Order (OPEN - not yet executed)
    (4001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1004', @adaPositionId, 'TestUser',
     'ADA/USDT:USDT', 'buy', 0.55, 0.52, 12500, 8, 0,
     1, 'OPEN', 'Initial Entry Order', DATEADD(hour, -12, GETUTCDATE())),
     
    -- ADA Order 2: DCA1 Entry Order (PENDING - will become OPEN after entry executes)
    (4002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1004', @adaPositionId, 'TestUser',
     'ADA/USDT:USDT', 'buy', 0.539, 0.52, 5000, 8, 0,
     1, 'PENDING', 'DCA1 Entry Order', DATEADD(hour, -11, GETUTCDATE())),
     
    -- ADA Order 3: DCA2 Entry Order (PENDING - will become OPEN after entry executes)
    (4003, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1004', @adaPositionId, 'TestUser',
     'ADA/USDT:USDT', 'buy', 0.528, 0.52, 7500, 8, 0,
     1, 'PENDING', 'DCA2 Entry Order', DATEADD(hour, -10, GETUTCDATE())),
     
    -- ADA Order 4: Stoploss Order (PENDING - will become OPEN after entry executes)
    (4004, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1004', @adaPositionId, 'TestUser',
     'ADA/USDT:USDT', 'sell', 0.52, 0.52, 25000, 8, 0,
     1, 'PENDING', 'Stoploss Order', DATEADD(hour, -9, GETUTCDATE())),
     
    -- ADA Order 5: Take Profit Order 1 (PENDING - will become OPEN after entry executes)
    (4005, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1004', @adaPositionId, 'TestUser',
     'ADA/USDT:USDT', 'sell', 0.5665, 0, 10000, 8, 0,
     1, 'PENDING', 'Take Profit Order 1', DATEADD(hour, -8, GETUTCDATE())),
     
    -- ADA Order 6: Take Profit Order 2 + MSL (PENDING - will become OPEN after entry executes)
    (4006, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1004', @adaPositionId, 'TestUser',
     'ADA/USDT:USDT', 'sell', 0.583, 0, 15000, 8, 0,
     1, 'PENDING', 'Take Profit Order 2 + MSL', DATEADD(hour, -7, GETUTCDATE()));

-- Create orders for BNB position (OPEN, entry NOT yet executed - PENDING orders for DCA/TP/SL)
INSERT INTO [AutoSignals].[dbo].[Orders] 
    ([SignalId], [UserId], [ExchangeId], [TelegramId], [PositionId], [UserName], 
     [Symbol], [Side], [Price], [Stoploss], [Size], [Leverage], [IsIsolated], 
     [IsTest], [Status], [Description], [Time])
VALUES
    -- BNB Order 1: Initial Entry Order (OPEN - not yet executed) - SHORT
    (5001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1005', @bnbPositionId, 'TestUser',
     'BNB/USDT:USDT', 'sell', 350.00, 360.00, 7.5, 15, 0,
     1, 'OPEN', 'Initial Entry Order', DATEADD(hour, -6, GETUTCDATE())),
     
    -- BNB Order 2: DCA1 Entry Order (PENDING) - SHORT
    (5002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1005', @bnbPositionId, 'TestUser',
     'BNB/USDT:USDT', 'sell', 357.00, 360.00, 3.0, 15, 0,
     1, 'PENDING', 'DCA1 Entry Order', DATEADD(hour, -5, GETUTCDATE())),
     
    -- BNB Order 3: DCA2 Entry Order (PENDING) - SHORT
    (5003, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1005', @bnbPositionId, 'TestUser',
     'BNB/USDT:USDT', 'sell', 364.00, 360.00, 4.5, 15, 0,
     1, 'PENDING', 'DCA2 Entry Order', DATEADD(hour, -4, GETUTCDATE())),
     
    -- BNB Order 4: Stoploss Order (PENDING) - For SHORT this is a BUY
    (5004, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1005', @bnbPositionId, 'TestUser',
     'BNB/USDT:USDT', 'buy', 360.00, 360.00, 15, 15, 0,
     1, 'PENDING', 'Stoploss Order', DATEADD(hour, -3, GETUTCDATE())),
     
    -- BNB Order 5: Take Profit Order 1 (PENDING) - For SHORT this is a BUY
    (5005, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1005', @bnbPositionId, 'TestUser',
     'BNB/USDT:USDT', 'buy', 339.50, 0, 6, 15, 0,
     1, 'PENDING', 'Take Profit Order 1', DATEADD(hour, -2, GETUTCDATE())),
     
    -- BNB Order 6: Take Profit Order 2 + MSL (PENDING) - For SHORT this is a BUY
    (5006, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1005', @bnbPositionId, 'TestUser',
     'BNB/USDT:USDT', 'buy', 329.00, 0, 9, 15, 0,
     1, 'PENDING', 'Take Profit Order 2 + MSL', DATEADD(hour, -1, GETUTCDATE()));

-- Create orders for historical CLOSED positions (with proper order flow)
INSERT INTO [AutoSignals].[dbo].[Orders] 
    ([SignalId], [UserId], [ExchangeId], [TelegramId], [PositionId], [UserName], 
     [Symbol], [Side], [Price], [Stoploss], [Size], [Leverage], [IsIsolated], 
     [IsTest], [Status], [Description], [Time])
VALUES
    -- BTC Closed Position Orders (7 days ago) - TP exit
    (6001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1006', @btcClosed7dPositionId, 'TestUser',
     'BTC/USDT:USDT', 'buy', 49000.00, 47500.00, 1.0, 8, 0,
     1, 'EXECUTED', 'Initial Entry Order', DATEADD(day, -7, GETUTCDATE())),
     
    (6002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1006', @btcClosed7dPositionId, 'TestUser',
     'BTC/USDT:USDT', 'buy', 48500.00, 47500.00, 0.5, 8, 0,
     1, 'EXECUTED', 'DCA1 Entry Order (Filled)', DATEADD(day, -6, GETUTCDATE())),
     
    (6003, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1006', @btcClosed7dPositionId, 'TestUser',
     'BTC/USDT:USDT', 'sell', 53165.00, 0, 1.5, 8, 0,
     1, 'EXECUTED', 'Take Profit Order (Closed Position)', DATEADD(day, -6, GETUTCDATE())),
     
    -- ETH Closed Short Position Orders (15 days ago) - SL exit
    (7001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1007', @ethClosed15dPositionId, 'TestUser',
     'ETH/USDT:USDT', 'sell', 3100.00, 3200.00, 10, 10, 0,
     1, 'EXECUTED', 'Initial Entry Order', DATEADD(day, -15, GETUTCDATE())),
     
    (7002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1007', @ethClosed15dPositionId, 'TestUser',
     'ETH/USDT:USDT', 'buy', 3200.00, 3200.00, 10, 10, 0,
     1, 'EXECUTED', 'Stoploss Triggered', DATEADD(day, -14, GETUTCDATE())),
     
    -- SOL Closed Position Orders (30 days ago) - SL exit with CANCELED DCA
    (8001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1008', @solClosed30dPositionId, 'TestUser',
     'SOL/USDT:USDT', 'buy', 95.00, 90.00, 5000, 15, 0,
     1, 'EXECUTED', 'Initial Entry Order', DATEADD(day, -30, GETUTCDATE())),
     
    (8002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1008', @solClosed30dPositionId, 'TestUser',
     'SOL/USDT:USDT', 'buy', 92.00, 90.00, 2000, 15, 0,
     1, 'CANCELED', 'DCA1 Entry Order (Canceled before fill)', DATEADD(day, -29, GETUTCDATE())),
     
    (8003, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1008', @solClosed30dPositionId, 'TestUser',
     'SOL/USDT:USDT', 'sell', 90.00, 90.00, 5000, 15, 0,
     1, 'EXECUTED', 'Stoploss Triggered', DATEADD(day, -29, GETUTCDATE())),
     
    -- ADA Closed Short Position Orders (45 days ago) - TP exit
    (9001, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1009', @adaClosed45dPositionId, 'TestUser',
     'ADA/USDT:USDT', 'sell', 0.60, 0.63, 25000, 5, 0,
     1, 'EXECUTED', 'Initial Entry Order', DATEADD(day, -45, GETUTCDATE())),
     
    (9002, '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d', '1', '1009', @adaClosed45dPositionId, 'TestUser',
     'ADA/USDT:USDT', 'buy', 0.5598, 0, 25000, 5, 0,
     1, 'EXECUTED', 'Take Profit Order', DATEADD(day, -44, GETUTCDATE()));

-- Verify the data
SELECT '=== POSITIONS SUMMARY ===' as Section;
SELECT 
    COUNT(*) as TotalPositions,
    SUM(CASE WHEN Status = 'OPEN' THEN 1 ELSE 0 END) as OpenPositions,
    SUM(CASE WHEN Status = 'CLOSED' THEN 1 ELSE 0 END) as ClosedPositions,
    SUM(CASE WHEN Side = 'buy' THEN 1 ELSE 0 END) as LongPositions,
    SUM(CASE WHEN Side = 'sell' THEN 1 ELSE 0 END) as ShortPositions
FROM [AutoSignals].[dbo].[Positions]
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d';

SELECT '=== ORDERS SUMMARY ===' as Section;
SELECT 
    COUNT(*) as TotalOrders,
    SUM(CASE WHEN Status = 'EXECUTED' THEN 1 ELSE 0 END) as ExecutedOrders,
    SUM(CASE WHEN Status = 'OPEN' THEN 1 ELSE 0 END) as OpenOrders,
    SUM(CASE WHEN Status = 'PENDING' THEN 1 ELSE 0 END) as PendingOrders,
    SUM(CASE WHEN Status = 'CANCELED' THEN 1 ELSE 0 END) as CanceledOrders,
    SUM(CASE WHEN Side = 'buy' THEN 1 ELSE 0 END) as BuyOrders,
    SUM(CASE WHEN Side = 'sell' THEN 1 ELSE 0 END) as SellOrders
FROM [AutoSignals].[dbo].[Orders]
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d';

-- Show positions by date range for testing filters
SELECT '=== POSITIONS BY DATE RANGE ===' as Info, 'Last 7 days' as TimeFrame,
    COUNT(*) as PositionCount,
    SUM(CASE WHEN Status = 'OPEN' THEN 1 ELSE 0 END) as OpenPositions,
    SUM(CASE WHEN Status = 'CLOSED' THEN 1 ELSE 0 END) as ClosedPositions
FROM [AutoSignals].[dbo].[Positions]
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d'
AND [Time] >= DATEADD(day, -7, GETUTCDATE());

SELECT '=== POSITIONS BY DATE RANGE ===' as Info, 'Last 30 days' as TimeFrame,
    COUNT(*) as PositionCount,
    SUM(CASE WHEN Status = 'OPEN' THEN 1 ELSE 0 END) as OpenPositions,
    SUM(CASE WHEN Status = 'CLOSED' THEN 1 ELSE 0 END) as ClosedPositions
FROM [AutoSignals].[dbo].[Positions]
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d'
AND [Time] >= DATEADD(day, -30, GETUTCDATE());

SELECT '=== POSITIONS BY DATE RANGE ===' as Info, 'Last 90 days' as TimeFrame,
    COUNT(*) as PositionCount,
    SUM(CASE WHEN Status = 'OPEN' THEN 1 ELSE 0 END) as OpenPositions,
    SUM(CASE WHEN Status = 'CLOSED' THEN 1 ELSE 0 END) as ClosedPositions
FROM [AutoSignals].[dbo].[Positions]
WHERE UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d'
AND [Time] >= DATEADD(day, -90, GETUTCDATE());

-- Detailed view of all positions with order workflow examples
SELECT '=== POSITION DETAILS WITH ORDER WORKFLOW ===' as Section;
SELECT 
    p.Id as PositionId,
    p.Symbol,
    p.Side as PositionSide,
    p.Status as PositionStatus,
    DATEDIFF(day, p.[Time], GETUTCDATE()) as DaysAgo,
    CASE 
        WHEN p.Status = 'OPEN' AND EXISTS (SELECT 1 FROM [AutoSignals].[dbo].[Orders] o WHERE o.PositionId = CAST(p.Id AS NVARCHAR(MAX)) AND o.Status = 'EXECUTED') THEN 'Entry EXECUTED, other orders OPEN'
        WHEN p.Status = 'OPEN' AND NOT EXISTS (SELECT 1 FROM [AutoSignals].[dbo].[Orders] o WHERE o.PositionId = CAST(p.Id AS NVARCHAR(MAX)) AND o.Status = 'EXECUTED') THEN 'Entry OPEN, other orders PENDING'
        WHEN p.Status = 'CLOSED' THEN 'Position CLOSED, all orders EXECUTED/CANCELED'
    END as WorkflowState,
    COUNT(o.Id) as TotalOrders,
    SUM(CASE WHEN o.Status = 'EXECUTED' THEN 1 ELSE 0 END) as ExecutedOrders,
    SUM(CASE WHEN o.Status = 'OPEN' THEN 1 ELSE 0 END) as OpenOrders,
    SUM(CASE WHEN o.Status = 'PENDING' THEN 1 ELSE 0 END) as PendingOrders,
    SUM(CASE WHEN o.Status = 'CANCELED' THEN 1 ELSE 0 END) as CanceledOrders
FROM [AutoSignals].[dbo].[Positions] p
LEFT JOIN [AutoSignals].[dbo].[Orders] o ON CAST(p.Id AS NVARCHAR(MAX)) = o.PositionId
WHERE p.UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d'
GROUP BY p.Id, p.Symbol, p.Side, p.Status, p.[Time]
ORDER BY p.[Time] DESC;

-- Example queries to test different scenarios:

-- 1. Find all positions with entry executed but position still OPEN
SELECT '=== Positions with entry EXECUTED but still OPEN ===' as Query;
SELECT 
    p.Id,
    p.Symbol,
    p.Side,
    p.Entry,
    p.ROI,
    COUNT(o.Id) as TotalOrders,
    SUM(CASE WHEN o.Description LIKE '%Initial%' AND o.Status = 'EXECUTED' THEN 1 ELSE 0 END) as EntryExecuted
FROM [AutoSignals].[dbo].[Positions] p
LEFT JOIN [AutoSignals].[dbo].[Orders] o ON CAST(p.Id AS NVARCHAR(MAX)) = o.PositionId
WHERE p.UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d'
AND p.Status = 'OPEN'
GROUP BY p.Id, p.Symbol, p.Side, p.Entry, p.ROI
HAVING SUM(CASE WHEN o.Description LIKE '%Initial%' AND o.Status = 'EXECUTED' THEN 1 ELSE 0 END) > 0;

-- 2. Find all orders that are PENDING (waiting for entry execution)
SELECT '=== Orders with PENDING status ===' as Query;
SELECT 
    p.Symbol,
    p.Status as PositionStatus,
    o.Description,
    o.Status as OrderStatus,
    o.Side,
    o.Price,
    o.Size
FROM [AutoSignals].[dbo].[Orders] o
JOIN [AutoSignals].[dbo].[Positions] p ON o.PositionId = CAST(p.Id AS NVARCHAR(MAX))
WHERE o.UserId = '3eb9dc48-a44a-41a0-ba95-3fcd6ac64a8d'
AND o.Status = 'PENDING'
ORDER BY p.Symbol, o.Description;