# MyAggregateRootSample

CAUTION: contains ai generated code

TODO: add unit tests 





## How to Use These Scripts:

### 1. **Basic Setup (Development)**
```bash
# Connect to PostgreSQL as admin
psql -U postgres -h localhost

# Create database
CREATE DATABASE customer_domain;

# Connect to the new database
\c customer_domain

# Run the table creation scripts (sections 3-4 from the artifact)
```

### 2. **Production Setup**
```bash
# Create database with specific settings
psql -U postgres -h localhost -f setup_database.sql

# Connect and run schema
psql -U postgres -h localhost -d customer_domain -f setup_schema.sql
```

### 3. **Docker Compose Setup**
```yaml
version: '3.8'
services:
  postgres:
    image: postgres:15
    environment:
      POSTGRES_DB: customer_domain
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - ./scripts:/docker-entrypoint-initdb.d/
```

### 4. **Connection String Examples**
```csharp
// Development
"Host=localhost;Database=customer_domain;Username=postgres;Password=postgres"

// Production with connection pooling
"Host=prod-server;Database=customer_domain;Username=customer_app;Password=secure_password;Pooling=true;MinPoolSize=5;MaxPoolSize=100"

// With SSL
"Host=prod-server;Database=customer_domain;Username=customer_app;Password=secure_password;SslMode=Require"
```

### 5. **Key Features of This Schema**

**Tables:**
- `customers` - Aggregate root storage with JSONB addresses
- `orders` - Order entities with addresses per order
- `order_items` - Individual line items with constraints
- `domain_events` - Outbox pattern with retry logic

**Indexes:**
- Performance indexes on foreign keys and query fields
- Partial index on unprocessed events for outbox pattern

**Views:**
- `customer_summary` - Customer statistics
- `order_details` - Complete order information
- `unprocessed_domain_events` - Events needing processing

**Triggers:**
- Automatic `updated_at` timestamp updates

**Constraints:**
- Referential integrity with cascade deletes
- Business rule constraints (positive quantities/prices)

This schema is production-ready with proper indexing, constraints, and monitoring capabilities built in.












```sql
-- PostgreSQL Database Setup Scripts for Customer Domain
-- Run these scripts in order to set up your database

-- =============================================================================
-- 1. CREATE DATABASE (Run as superuser/admin)
-- =============================================================================

-- Create database (if it doesn't exist)
-- Note: This must be run outside of a transaction block
-- SELECT 'CREATE DATABASE customer_domain' 
-- WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'customer_domain');

CREATE DATABASE customer_domain
    WITH 
    OWNER = postgres
    ENCODING = 'UTF8'
    LC_COLLATE = 'en_US.utf8'
    LC_CTYPE = 'en_US.utf8'
    TABLESPACE = pg_default
    CONNECTION LIMIT = -1;

-- =============================================================================
-- 2. CREATE SCHEMA AND TABLES (Run after connecting to the database)
-- =============================================================================

-- Connect to the customer_domain database first
-- \c customer_domain

-- Create schema (optional - you can use public schema)
CREATE SCHEMA IF NOT EXISTS customer_domain;

-- Set search path to include our schema
SET search_path TO customer_domain, public;

-- =============================================================================
-- 3. CREATE TABLES
-- =============================================================================

-- Create customers table
CREATE TABLE IF NOT EXISTS customers (
    id UUID PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    default_shipping_address_json JSONB,
    default_billing_address_json JSONB,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Add comments for documentation
COMMENT ON TABLE customers IS 'Customer aggregate root storage';
COMMENT ON COLUMN customers.id IS 'Unique identifier for the customer';
COMMENT ON COLUMN customers.name IS 'Customer display name';
COMMENT ON COLUMN customers.default_shipping_address_json IS 'JSON representation of default shipping address';
COMMENT ON COLUMN customers.default_billing_address_json IS 'JSON representation of default billing address';
COMMENT ON COLUMN customers.created_at IS 'When the customer record was created';
COMMENT ON COLUMN customers.updated_at IS 'When the customer record was last updated';

-- Create orders table
CREATE TABLE IF NOT EXISTS orders (
    id UUID PRIMARY KEY,
    customer_id UUID NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    order_date TIMESTAMP WITH TIME ZONE NOT NULL,
    shipping_address_json JSONB NOT NULL,
    billing_address_json JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Add comments for documentation
COMMENT ON TABLE orders IS 'Customer orders';
COMMENT ON COLUMN orders.id IS 'Unique identifier for the order';
COMMENT ON COLUMN orders.customer_id IS 'Reference to the customer who placed the order';
COMMENT ON COLUMN orders.order_date IS 'When the order was placed';
COMMENT ON COLUMN orders.shipping_address_json IS 'JSON representation of shipping address for this order';
COMMENT ON COLUMN orders.billing_address_json IS 'JSON representation of billing address for this order';
COMMENT ON COLUMN orders.created_at IS 'When the order record was created';
COMMENT ON COLUMN orders.updated_at IS 'When the order record was last updated';

-- Create order_items table
CREATE TABLE IF NOT EXISTS order_items (
    id UUID PRIMARY KEY,
    order_id UUID NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product VARCHAR(255) NOT NULL,
    quantity INTEGER NOT NULL CHECK (quantity > 0),
    price DECIMAL(10,2) NOT NULL CHECK (price > 0),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Add comments for documentation
COMMENT ON TABLE order_items IS 'Individual items within orders';
COMMENT ON COLUMN order_items.id IS 'Unique identifier for the order item';
COMMENT ON COLUMN order_items.order_id IS 'Reference to the order this item belongs to';
COMMENT ON COLUMN order_items.product IS 'Product name/description';
COMMENT ON COLUMN order_items.quantity IS 'Quantity of the product ordered (must be positive)';
COMMENT ON COLUMN order_items.price IS 'Unit price of the product (must be positive)';
COMMENT ON COLUMN order_items.created_at IS 'When the order item was created';

-- Create domain_events table (outbox pattern)
CREATE TABLE IF NOT EXISTS domain_events (
    id UUID PRIMARY KEY,
    aggregate_id UUID NOT NULL,
    event_type VARCHAR(255) NOT NULL,
    event_data JSONB NOT NULL,
    occurred_on TIMESTAMP WITH TIME ZONE NOT NULL,
    processed BOOLEAN NOT NULL DEFAULT FALSE,
    processed_at TIMESTAMP WITH TIME ZONE NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT NULL
);

-- Add comments for documentation
COMMENT ON TABLE domain_events IS 'Outbox pattern storage for domain events';
COMMENT ON COLUMN domain_events.id IS 'Unique identifier for the domain event';
COMMENT ON COLUMN domain_events.aggregate_id IS 'ID of the aggregate root that generated the event';
COMMENT ON COLUMN domain_events.event_type IS 'Type name of the domain event';
COMMENT ON COLUMN domain_events.event_data IS 'JSON serialized event data';
COMMENT ON COLUMN domain_events.occurred_on IS 'When the event occurred';
COMMENT ON COLUMN domain_events.processed IS 'Whether the event has been processed';
COMMENT ON COLUMN domain_events.processed_at IS 'When the event was processed';
COMMENT ON COLUMN domain_events.retry_count IS 'Number of processing attempts';
COMMENT ON COLUMN domain_events.last_error IS 'Last error message if processing failed';

-- =============================================================================
-- 4. CREATE INDEXES
-- =============================================================================

-- Indexes for customers table
CREATE INDEX IF NOT EXISTS idx_customers_name ON customers(name);
CREATE INDEX IF NOT EXISTS idx_customers_created_at ON customers(created_at);

-- Indexes for orders table
CREATE INDEX IF NOT EXISTS idx_orders_customer_id ON orders(customer_id);
CREATE INDEX IF NOT EXISTS idx_orders_order_date ON orders(order_date);
CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders(created_at);

-- Indexes for order_items table
CREATE INDEX IF NOT EXISTS idx_order_items_order_id ON order_items(order_id);
CREATE INDEX IF NOT EXISTS idx_order_items_product ON order_items(product);

-- Indexes for domain_events table
CREATE INDEX IF NOT EXISTS idx_domain_events_aggregate_id ON domain_events(aggregate_id);
CREATE INDEX IF NOT EXISTS idx_domain_events_processed ON domain_events(processed);
CREATE INDEX IF NOT EXISTS idx_domain_events_occurred_on ON domain_events(occurred_on);
CREATE INDEX IF NOT EXISTS idx_domain_events_event_type ON domain_events(event_type);
CREATE INDEX IF NOT EXISTS idx_domain_events_unprocessed ON domain_events(processed, occurred_on) WHERE processed = FALSE;

-- =============================================================================
-- 5. CREATE TRIGGERS (Optional - for automatic updated_at timestamps)
-- =============================================================================

-- Function to update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Triggers for automatic updated_at timestamps
CREATE TRIGGER update_customers_updated_at 
    BEFORE UPDATE ON customers 
    FOR EACH ROW 
    EXECUTE FUNCTION update_updated_at_column();

CREATE TRIGGER update_orders_updated_at 
    BEFORE UPDATE ON orders 
    FOR EACH ROW 
    EXECUTE FUNCTION update_updated_at_column();

-- =============================================================================
-- 6. CREATE VIEWS (Optional - for reporting and queries)
-- =============================================================================

-- View for customer summary
CREATE OR REPLACE VIEW customer_summary AS
SELECT 
    c.id,
    c.name,
    c.created_at,
    c.updated_at,
    COUNT(o.id) as total_orders,
    COALESCE(SUM(oi.quantity * oi.price), 0) as total_order_value,
    MAX(o.order_date) as last_order_date
FROM customers c
LEFT JOIN orders o ON c.id = o.customer_id
LEFT JOIN order_items oi ON o.id = oi.order_id
GROUP BY c.id, c.name, c.created_at, c.updated_at;

COMMENT ON VIEW customer_summary IS 'Summary view of customers with order statistics';

-- View for order details
CREATE OR REPLACE VIEW order_details AS
SELECT 
    o.id as order_id,
    o.customer_id,
    c.name as customer_name,
    o.order_date,
    o.shipping_address_json,
    o.billing_address_json,
    COUNT(oi.id) as item_count,
    SUM(oi.quantity * oi.price) as total_amount,
    o.created_at,
    o.updated_at
FROM orders o
JOIN customers c ON o.customer_id = c.id
LEFT JOIN order_items oi ON o.id = oi.order_id
GROUP BY o.id, o.customer_id, c.name, o.order_date, o.shipping_address_json, o.billing_address_json, o.created_at, o.updated_at;

COMMENT ON VIEW order_details IS 'Detailed view of orders with customer information and totals';

-- View for unprocessed domain events
CREATE OR REPLACE VIEW unprocessed_domain_events AS
SELECT 
    id,
    aggregate_id,
    event_type,
    occurred_on,
    retry_count,
    last_error
FROM domain_events
WHERE processed = FALSE
ORDER BY occurred_on ASC;

COMMENT ON VIEW unprocessed_domain_events IS 'View of domain events that need processing';

-- =============================================================================
-- 7. GRANT PERMISSIONS (Adjust based on your security requirements)
-- =============================================================================

-- Create application user (replace with your actual username)
-- CREATE USER customer_app WITH PASSWORD 'your_secure_password';

-- Grant necessary permissions
-- GRANT CONNECT ON DATABASE customer_domain TO customer_app;
-- GRANT USAGE ON SCHEMA customer_domain TO customer_app;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA customer_domain TO customer_app;
-- GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA customer_domain TO customer_app;

-- =============================================================================
-- 8. SAMPLE DATA (Optional - for testing)
-- =============================================================================

-- Insert sample customer
INSERT INTO customers (id, name, default_shipping_address_json, default_billing_address_json, created_at, updated_at)
VALUES (
    '550e8400-e29b-41d4-a716-446655440000',
    'John Doe',
    '{"Street":"123 Main St","City":"Anytown","State":"CA","PostalCode":"12345","Country":"USA"}',
    '{"Street":"456 Billing Ave","City":"Billing City","State":"CA","PostalCode":"54321","Country":"USA"}',
    NOW(),
    NOW()
) ON CONFLICT (id) DO NOTHING;

-- Insert sample order
INSERT INTO orders (id, customer_id, order_date, shipping_address_json, billing_address_json, created_at, updated_at)
VALUES (
    '550e8400-e29b-41d4-a716-446655440001',
    '550e8400-e29b-41d4-a716-446655440000',
    NOW() - INTERVAL '1 day',
    '{"Street":"123 Main St","City":"Anytown","State":"CA","PostalCode":"12345","Country":"USA"}',
    '{"Street":"456 Billing Ave","City":"Billing City","State":"CA","PostalCode":"54321","Country":"USA"}',
    NOW(),
    NOW()
) ON CONFLICT (id) DO NOTHING;

-- Insert sample order items
INSERT INTO order_items (id, order_id, product, quantity, price, created_at)
VALUES 
    ('550e8400-e29b-41d4-a716-446655440002', '550e8400-e29b-41d4-a716-446655440001', 'Widget A', 2, 19.99, NOW()),
    ('550e8400-e29b-41d4-a716-446655440003', '550e8400-e29b-41d4-a716-446655440001', 'Widget B', 1, 29.99, NOW())
ON CONFLICT (id) DO NOTHING;

-- =============================================================================
-- 9. VERIFICATION QUERIES
-- =============================================================================

-- Verify tables were created
SELECT table_name, table_type 
FROM information_schema.tables 
WHERE table_schema = 'customer_domain' OR table_schema = 'public'
ORDER BY table_name;

-- Verify indexes were created
SELECT indexname, tablename, indexdef 
FROM pg_indexes 
WHERE schemaname = 'customer_domain' OR schemaname = 'public'
ORDER BY tablename, indexname;

-- Verify sample data
SELECT 'customers' as table_name, COUNT(*) as record_count FROM customers
UNION ALL
SELECT 'orders' as table_name, COUNT(*) as record_count FROM orders
UNION ALL
SELECT 'order_items' as table_name, COUNT(*) as record_count FROM order_items
UNION ALL
SELECT 'domain_events' as table_name, COUNT(*) as record_count FROM domain_events;

-- Test the views
SELECT * FROM customer_summary;
SELECT * FROM order_details;
SELECT * FROM unprocessed_domain_events;

-- =============================================================================
-- 10. MAINTENANCE QUERIES (Optional - for ongoing database maintenance)
-- =============================================================================

-- Query to find customers with no orders
SELECT c.id, c.name, c.created_at
FROM customers c
LEFT JOIN orders o ON c.id = o.customer_id
WHERE o.id IS NULL;

-- Query to find orders with no items
SELECT o.id, o.customer_id, o.order_date
FROM orders o
LEFT JOIN order_items oi ON o.id = oi.order_id
WHERE oi.id IS NULL;

-- Query to find domain events that failed processing multiple times
SELECT id, aggregate_id, event_type, retry_count, last_error, occurred_on
FROM domain_events
WHERE processed = FALSE AND retry_count > 3
ORDER BY occurred_on DESC;

-- Query to clean up old processed domain events (older than 30 days)
-- DELETE FROM domain_events 
-- WHERE processed = TRUE AND processed_at < NOW() - INTERVAL '30 days';

-- =============================================================================
-- 11. PERFORMANCE MONITORING QUERIES
-- =============================================================================

-- Check table sizes
SELECT 
    schemaname,
    tablename,
    attname,
    n_distinct,
    correlation
FROM pg_stats
WHERE schemaname IN ('customer_domain', 'public')
ORDER BY schemaname, tablename, attname;

-- Check index usage
SELECT 
    schemaname,
    tablename,
    indexname,
    idx_scan,
    idx_tup_read,
    idx_tup_fetch
FROM pg_stat_user_indexes
WHERE schemaname IN ('customer_domain', 'public')
ORDER BY idx_scan DESC;

-- Check table statistics
SELECT 
    schemaname,
    tablename,
    n_tup_ins,
    n_tup_upd,
    n_tup_del,
    n_live_tup,
    n_dead_tup,
    last_vacuum,
    last_autovacuum,
    last_analyze,
    last_autoanalyze
FROM pg_stat_user_tables
WHERE schemaname IN ('customer_domain', 'public')
ORDER BY schemaname, tablename;
```